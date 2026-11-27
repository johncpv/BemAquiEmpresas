using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

public sealed partial class SalesOrderService
{
    public const string BankActor = "Banco Inter (baixa do boleto)";
    public const string ExpiryActor = "Prazo de pagamento";
    public const string ExpiryReason = "Prazo de pagamento vencido";
    public const string NoSlipMethodMessage = "Configure a forma de pagamento do boleto em Configurações → Parâmetros.";

    /// <summary>Diálogo "Gerar cobrança" já preenchido (CA 15 a 17).</summary>
    public async Task<Result<SalesOrderChargeDraft>> ChargeDraftAsync(Guid id, SalesOrderActor actor, CancellationToken cancellationToken = default)
    {
        var order = await db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null || (actor.IsApp && order.SourceAppId != actor.SourceAppId))
        {
            return NotFound();
        }

        var settings = await parameters.CurrentAsync(cancellationToken);
        var readiness = await charges.ReadinessAsync(order.CustomerId, cancellationToken);
        var due = SalesOrderRules.AddBusinessDays(Today, actor.IsApp ? Math.Min(settings.AppChargeDays, SalesOrderRules.MaxAppChargeDays) : settings.ChargeDays);
        return new SalesOrderChargeDraft(order.Total, due, readiness.Email, readiness.IntegrationReady, await parameters.BankSlipMethodAsync(cancellationToken) is not null,
            readiness.CustomerBillable, readiness.Missing, order.CustomerId, order.Version);
    }

    /// <summary>
    /// "Gerar cobrança" (CA 15 a 17): o Financeiro cria o título <c>SalesOrder</c> e pede o boleto (transação própria, idempotente pelo pedido); depois o
    /// pedido vai a <c>AwaitingPayment</c> com a reserva valendo até o vencimento + 1 dia útil. Nenhuma cobrança sai sem documento e endereço.
    /// </summary>
    public async Task<Result<SalesOrderDetail>> ChargeAsync(Guid id, SalesOrderChargeInput input, SalesOrderActor actor, Guid? clientRequestId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actor);
        var loaded = await LoadAsync(id, input.Version, actor, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (!SalesOrderRules.Can(order.Status, SalesOrderAction.Charge))
        {
            return InvalidTransition(order, SalesOrderAction.Charge);
        }

        if (await parameters.BankSlipMethodAsync(cancellationToken) is null)
        {
            return Error.Conflict("bank_slip_method_missing", NoSlipMethodMessage);
        }

        var readiness = await charges.ReadinessAsync(order.CustomerId, cancellationToken);
        if (!readiness.CustomerBillable)
        {
            return Error.Validation("customer_not_billable", "Para gerar a cobrança, complete o cadastro do cliente: " + string.Join("; ", readiness.Missing) + ".");
        }

        var maxDue = actor.IsApp ? SalesOrderRules.AddBusinessDays(Today, SalesOrderRules.MaxAppChargeDays) : Today.AddDays(60);
        if (input.DueDate < Today || input.DueDate > maxDue)
        {
            return Field("dueDate", actor.IsApp
                ? $"O vencimento do pedido de aplicativo vai de hoje até {SalesOrderRules.MaxAppChargeDays} dias úteis."
                : "O vencimento da cobrança vai de hoje até 60 dias.");
        }

        var charged = await charges.ChargeAsync(new OrderChargeRequest(order.Id, order.Number, order.CustomerId, order.Total, input.DueDate, input.SendEmail, clientRequestId),
            cancellationToken);
        if (charged.IsFailure)
        {
            return charged.Error!;
        }

        return await InTransactionAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var hold = BusinessTime.StartOfDayUtc(SalesOrderRules.ChargeHoldDate(input.DueDate).AddDays(1));
            order.Charged(charged.Value.ReceivableId, hold, now);
            await reservations.SetExpiryAsync(SalesOrderRules.SourceType, order.Id, hold, cancellationToken);
            Record(order, SalesOrderStatus.Open, "charge", actor, "Vencimento " + input.DueDate.ToString("dd/MM/yyyy", PtBr), now);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(order.Id);
        }, cancellationToken);
    }

    /// <summary>
    /// "Registrar pagamento" (CA 20, <c>sales.approve</c>): com boleto do pedido, reconsulta no Inter antes; pago lá, recusa. Senão, cancela boleto e
    /// título e o pedido vira venda pelo <see cref="IOrderPayments"/> com o provedor manual, tudo sobre o <c>xmin</c> do pedido.
    /// </summary>
    public async Task<Result<SalesOrderDetail>> RegisterPaymentAsync(Guid id, SalesOrderPaymentInput input, SalesOrderActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actor);
        var loaded = await LoadAsync(id, input.Version, actor, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (!SalesOrderRules.Can(order.Status, SalesOrderAction.Pay))
        {
            return InvalidTransition(order, SalesOrderAction.Pay);
        }

        var method = (await paymentMethods.ActiveAsync(Today, cancellationToken)).FirstOrDefault(m => m.Id == input.PaymentMethodId);
        if (method is null || method.Type is PaymentMethodType.BankSlip or PaymentMethodType.OnAccount or PaymentMethodType.StoreCredit)
        {
            return Field("paymentMethodId", "Escolha a forma em que o valor foi recebido (dinheiro, maquininha ou Pix).");
        }

        var received = SaleRules.Money(input.AmountReceived ?? order.Total);
        if (received < order.Total || (received > order.Total && !method.AllowsChange))
        {
            return Field("amountReceived", method.AllowsChange
                ? $"O valor recebido precisa ser pelo menos o total do pedido ({Money(order.Total)})."
                : $"O valor recebido em {method.Name} é o total do pedido ({Money(order.Total)}).");
        }

        if (order.ReceivableId is not null)
        {
            var recheck = await charges.RecheckSlipAsync(order.Id, cancellationToken);
            if (recheck.IsFailure)
            {
                return recheck.Error!;
            }

            if (recheck.Value.Paid)
            {
                var on = recheck.Value.PaidOn?.ToString("dd/MM/yyyy", PtBr) ?? "hoje";
                return Error.Conflict("slip_already_paid", $"Este boleto já foi pago em {on}. O pedido fica pago pela baixa do banco.");
            }
        }

        return await InTransactionAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var from = order.Status;
            await charges.CancelAsync(order.Id, "Pago por outro meio no pedido " + order.Number, paidOtherMeans: true, cancellationToken);
            var lines = await db.SalesOrderLines.Where(l => l.OrderId == order.Id).OrderBy(l => l.LineNumber).ToListAsync(cancellationToken);
            order.Pay(SalesOrderPaymentKind.Manual, now);
            var paid = await sales.PayAsync(new OrderPaymentRequest(
                new OrderSale(order.SaleId!.Value, SaleChannel.Online, order.Number, SalesOrderRules.SourceType, order.Id, SaleLines(order, lines), order.StoreId, order.StockLocationId,
                    order.CustomerId, order.CreatedBy),
                [new OrderPaymentInput(Guid.CreateVersion7(), method.Id, received, PaymentIntegration.Manual, SaleRules.Money(received - order.Total), CardBrand: Clean(input.CardBrand),
                    Nsu: Clean(input.Nsu))]), cancellationToken);
            if (paid.IsFailure)
            {
                return paid.Error!;
            }

            if (paid.Value.Status != SaleStatus.Completed)
            {
                return Error.Conflict("payment_not_captured", "O pagamento não foi confirmado. Confira a forma e tente de novo.");
            }

            await reservations.SetExpiryAsync(SalesOrderRules.SourceType, order.Id, null, cancellationToken);
            Record(order, from, "pay", actor, "Pagamento registrado: " + method.Name, now);
            events.Publish(new OrderPaid(order.Id, order.Number, order.Origin, order.SourceAppId, order.ExternalId, order.Status, order.Total, now, order.SaleId!.Value));
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(order.Id);
        }, cancellationToken);
    }

    /// <summary>
    /// "Cancelar pedido" (CA 25, <c>sales.edit</c>) no rascunho, aberto ou aguardando pagamento: libera a reserva e cancela título e boleto. No pago (CA 26,
    /// <c>sales.approve</c> e "Já devolvi o valor ao cliente"): estorna a venda (<c>SaleCancelled</c> devolve o estoque) e, pago por boleto, registra a
    /// devolução contra o título.
    /// </summary>
    public async Task<Result<SalesOrderDetail>> CancelAsync(Guid id, SalesOrderCancelInput input, SalesOrderActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actor);
        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 200)
        {
            return Field("reason", "Informe o motivo do cancelamento (de 3 a 200 caracteres).");
        }

        var loaded = await LoadAsync(id, input.Version, actor, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        var paid = order.Status == SalesOrderStatus.Paid;
        var action = paid ? SalesOrderAction.CancelPaid : SalesOrderAction.Cancel;
        if (!SalesOrderRules.Can(order.Status, action))
        {
            return InvalidTransition(order, SalesOrderAction.Cancel);
        }

        if (paid)
        {
            if (actor.IsApp || !await permissions.HasPermissionAsync(SaleService.Module, "approve", cancellationToken))
            {
                return Error.Forbidden(Error.Codes.Forbidden, "Só quem aprova em Vendas cancela pedido pago e estorna a venda.");
            }

            if (!input.RefundedOutside)
            {
                return Field("refundedOutside", "Marque \"Já devolvi o valor ao cliente\" para cancelar o pedido pago.");
            }
        }

        return await InTransactionAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var from = order.Status;
            if (paid)
            {
                var cancelled = await sales.CancelAsync(new SaleCancellationRequest(order.SaleId!.Value, reason, CapturedRefundedOutside: true), cancellationToken);
                if (cancelled.IsFailure)
                {
                    return cancelled.Error!;
                }

                if (order.PaymentKind == SalesOrderPaymentKind.BankSlip)
                {
                    var refunded = await charges.RefundAsync(order.Id, reason, cancellationToken);
                    if (refunded.IsFailure)
                    {
                        return refunded.Error!;
                    }
                }
            }
            else
            {
                await charges.CancelAsync(order.Id, reason, cancellationToken: cancellationToken);
            }

            await reservations.ReleaseAsync(SalesOrderRules.SourceType, order.Id, "Pedido " + order.Number + " cancelado", cancellationToken);
            order.Cancel(reason, now);
            Record(order, from, paid ? "cancel_paid" : "cancel", actor, reason, now);
            events.Publish(new OrderCancelled(order.Id, order.Number, order.Origin, order.SourceAppId, order.ExternalId, order.Status, order.Total, now));
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(order.Id);
        }, cancellationToken);
    }

    /// <summary>"Reabrir pedido" do pago depois de cancelado (CA 19, <c>sales.approve</c>): reserva de novo (sem saldo, recusa) e vira venda pelo boleto pago.</summary>
    public async Task<Result<SalesOrderDetail>> ReopenAsync(Guid id, SalesOrderVersionInput input, SalesOrderActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actor);
        var loaded = await LoadAsync(id, input.Version, actor, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (!SalesOrderRules.Can(order.Status, SalesOrderAction.Reopen) || !order.PaidAfterCancel)
        {
            return InvalidTransition(order, SalesOrderAction.Reopen);
        }

        if (order.SaleId is { } saleId && await db.Sales.AnyAsync(s => s.Id == saleId, cancellationToken))
        {
            return Error.Conflict("order_sale_reversed", "A venda deste pedido já foi estornada. Use \"Devolver o valor\".");
        }

        var charge = await charges.FindAsync(order.Id, cancellationToken);
        if (charge is not { Status: ReceivableStatus.Paid, RefundedAt: null })
        {
            return Error.Conflict("order_charge_not_paid", "Este pedido não tem pagamento por boleto a reaproveitar.");
        }

        var methodId = (await parameters.BankSlipMethodAsync(cancellationToken))?.Id;
        if (methodId is null)
        {
            return Error.Conflict("bank_slip_method_missing", NoSlipMethodMessage);
        }

        return await InTransactionAsync(async () =>
        {
            var now = clock.GetUtcNow();
            var lines = await db.SalesOrderLines.Where(l => l.OrderId == order.Id).OrderBy(l => l.LineNumber).ToListAsync(cancellationToken);
            var reserved = await ReserveAsync(order, lines, cancellationToken);
            if (reserved is not null)
            {
                return reserved;
            }

            return await PayByBankAsync(order, charge.ReceivableId, methodId.Value, actor, "reopen", null, now, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>"Devolver o valor" do pago depois de cancelado (CA 19, <c>sales.approve</c>): registra a devolução feita fora contra o título; nada é estornado sozinho.</summary>
    public async Task<Result<SalesOrderDetail>> RefundAsync(Guid id, SalesOrderVersionInput input, SalesOrderActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actor);
        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 200)
        {
            return Field("reason", "Informe como o valor foi devolvido (de 3 a 200 caracteres).");
        }

        var loaded = await LoadAsync(id, input.Version, actor, cancellationToken);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var order = loaded.Value;
        if (!order.PaidAfterCancel)
        {
            return InvalidTransition(order, SalesOrderAction.Refund);
        }

        return await InTransactionAsync(async () =>
        {
            var refunded = await charges.RefundAsync(order.Id, reason, cancellationToken);
            if (refunded.IsFailure)
            {
                return refunded.Error!;
            }

            var now = clock.GetUtcNow();
            order.PaidAfterCancel = false;
            order.UpdatedAt = now;
            Record(order, order.Status, "refund", actor, reason, now);
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success(order.Id);
        }, cancellationToken);
    }

    /// <summary>
    /// Baixa do título do pedido (CA 18, 19 e 21), na transação do consumidor: pedido aberto ou aguardando, vira venda uma vez só e publica
    /// <see cref="OrderPaid"/>; cancelado (ou já pago à mão), ganha o alerta "Pago depois de cancelado" para quem tem <c>sales.approve</c>.
    /// </summary>
    public async Task HandleSettledAsync(Guid receivableId, CancellationToken cancellationToken = default)
    {
        var order = await db.SalesOrders.FirstOrDefaultAsync(o => o.ReceivableId == receivableId, cancellationToken);
        if (order is null)
        {
            return;
        }

        var charge = await charges.FindAsync(order.Id, cancellationToken);
        if (charge is null || charge.ReceivableId != receivableId || charge.Status != ReceivableStatus.Paid)
        {
            return;
        }

        var bank = new SalesOrderActor(order.Origin, null);
        var now = clock.GetUtcNow();
        if (order.Status is SalesOrderStatus.Open or SalesOrderStatus.AwaitingPayment)
        {
            var methodId = (await parameters.BankSlipMethodAsync(cancellationToken))?.Id
                ?? throw new InvalidOperationException("Forma de pagamento do boleto não configurada: o pedido " + order.Number + " fica aguardando.");
            var paid = await PayByBankAsync(order, receivableId, methodId, bank, "pay", BankActor, now, cancellationToken);
            if (paid.IsFailure)
            {
                throw new InvalidOperationException("Conversão do pedido " + order.Number + " em venda recusada: " + paid.Error!.Message);
            }

            return;
        }

        if ((order.Status == SalesOrderStatus.Cancelled || order.PaymentKind == SalesOrderPaymentKind.Manual) && !order.PaidAfterCancel && charge.RefundedAt is null)
        {
            order.PaidAfterCancel = true;
            order.UpdatedAt = now;
            Record(order, order.Status, "paid_after_cancel", bank, "Boleto pago depois do cancelamento", now, BankActor);
            foreach (var user in await users.UsersWithPermissionAsync("sales.approve", cancellationToken))
            {
                notifications.Notify(user, NotificationType, $"O pedido {order.Number} foi pago depois de cancelado. Reabra o pedido ou registre a devolução.", Link(order.Id));
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Boleto cancelado ou vencido sem pagamento (CA 6): o pedido aguardando volta a aberto, com o prazo de reserva de pessoa ou de aplicativo.</summary>
    public async Task HandleSlipReleasedAsync(Guid receivableId, bool expired, CancellationToken cancellationToken = default)
    {
        var order = await db.SalesOrders.FirstOrDefaultAsync(o => o.ReceivableId == receivableId && o.Status == SalesOrderStatus.AwaitingPayment, cancellationToken);
        if (order is null || (await charges.FindAsync(order.Id, cancellationToken))?.Status == ReceivableStatus.Paid)
        {
            return;
        }

        var settings = await parameters.CurrentAsync(cancellationToken);
        var now = clock.GetUtcNow();
        order.ChargeLapsed(now.AddHours(order.FromApp ? settings.AppHoldHours : settings.HoldHours), now);
        await reservations.SetExpiryAsync(SalesOrderRules.SourceType, order.Id, order.HoldUntil, cancellationToken);
        Record(order, SalesOrderStatus.AwaitingPayment, "charge_lapsed", new SalesOrderActor(order.Origin, null), expired ? "Boleto vencido no Inter" : "Boleto cancelado",
            now, "Banco Inter");
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Job <c>sales-order-expiry</c> (CA 12): cancela os pedidos abertos ou aguardando com a reserva vencida (motivo "Prazo de pagamento vencido"), libera
    /// a reserva, cancela título e boleto e avisa o vendedor com "Recriar pedido". Pedido pago não expira. Devolve quantos cancelou.
    /// </summary>
    public async Task<int> ExpireAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var due = await db.SalesOrders.AsNoTracking()
            .Where(o => (o.Status == SalesOrderStatus.Open || o.Status == SalesOrderStatus.AwaitingPayment) && o.HoldUntil != null && o.HoldUntil < now)
            .OrderBy(o => o.HoldUntil).Select(o => o.Id).Take(200).ToListAsync(cancellationToken);
        var done = 0;
        foreach (var id in due)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var order = await db.SalesOrders.FirstAsync(o => o.Id == id, cancellationToken);
            if (order.Status is not (SalesOrderStatus.Open or SalesOrderStatus.AwaitingPayment) || order.HoldUntil >= now)
            {
                continue;
            }

            var from = order.Status;
            await charges.CancelAsync(order.Id, ExpiryReason, cancellationToken: cancellationToken);
            await reservations.ReleaseAsync(SalesOrderRules.SourceType, order.Id, ExpiryReason, cancellationToken);
            order.Cancel(ExpiryReason, now);
            Record(order, from, "expire", new SalesOrderActor(order.Origin, null), ExpiryReason, now, ExpiryActor);
            events.Publish(new OrderCancelled(order.Id, order.Number, order.Origin, order.SourceAppId, order.ExternalId, order.Status, order.Total, now));
            if (order.CreatedBy is { } seller)
            {
                notifications.Notify(seller, NotificationType, $"O pedido {order.Number} foi cancelado: prazo de pagamento vencido. Recrie o pedido se o cliente ainda quiser.",
                    "/app/loja/pedidos/novo?de=" + order.Id);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                done++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Pago ou cancelado ao mesmo tempo: fica para a próxima rodada, que relê o pedido.
                await transaction.RollbackAsync(cancellationToken);
            }
        }

        return done;
    }

    /// <summary>Pedido pago pelo boleto vira a venda (CA 22 e 23): mesma ingestão da VND-01, com o pagamento ligado ao título (não gera outro recebível).</summary>
    private async Task<Result<Guid>> PayByBankAsync(SalesOrder order, Guid receivableId, Guid methodId, SalesOrderActor actor, string action, string? label, DateTimeOffset now,
        CancellationToken ct)
    {
        var from = order.Status;
        var lines = await db.SalesOrderLines.Where(l => l.OrderId == order.Id).OrderBy(l => l.LineNumber).ToListAsync(ct);
        order.Pay(SalesOrderPaymentKind.BankSlip, now);
        var ingested = await sales.IngestAsync(new SaleIngestionRequest(order.SaleId!.Value, SaleChannel.Online, order.Number, SalesOrderRules.SourceType, order.Id, now,
            SaleLines(order, lines), [new SalePaymentInput(Guid.CreateVersion7(), methodId, order.Total, SalePaymentStatus.Captured, PaymentIntegration.Manual, Provider: "Inter",
                ReceivableId: receivableId)], StoreId: order.StoreId, StockLocationId: order.StockLocationId, CustomerId: order.CustomerId, SellerUserId: order.CreatedBy), ct);
        if (ingested.IsFailure)
        {
            return ingested.Error!;
        }

        await reservations.SetExpiryAsync(SalesOrderRules.SourceType, order.Id, null, ct);
        Record(order, from, action, actor, "Pago pelo boleto", now, label);
        events.Publish(new OrderPaid(order.Id, order.Number, order.Origin, order.SourceAppId, order.ExternalId, order.Status, order.Total, now, order.SaleId!.Value));
        await db.SaveChangesAsync(ct);
        return order.Id;
    }

    /// <summary>Linhas da venda: as do pedido, com o desconto do pedido quebrado entre elas (a venda não tem desconto de cabeçalho).</summary>
    private static List<SaleLineInput> SaleLines(SalesOrder order, IReadOnlyList<SalesOrderLine> lines)
    {
        var spread = SalesOrderRules.SpreadOrderDiscount(lines.Select(l => l.Total).ToList(), order.OrderDiscount);
        return lines.Select((l, i) => new SaleLineInput(Guid.CreateVersion7(), l.ProductId, l.Quantity, l.UnitPrice, SaleRules.Money(l.Discount + spread[i]))).ToList();
    }
}
