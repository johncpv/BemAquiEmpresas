using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

public sealed partial class SaleService
{
    /// <summary>
    /// Pagamento de pedido (CA 7): a venda nasce no primeiro pedido (ou é a já criada pelo mesmo <c>SaleId</c>); cada pagamento novo passa pelo
    /// provedor da integração e pela máquina de estados. O pagamento repetido (mesmo id) não é gravado de novo. Quando o capturado fecha o
    /// total, a venda conclui e publica <see cref="SaleCompleted"/>.
    /// </summary>
    public async Task<Result<SaleView>> PayAsync(OrderPaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var order = request.Order;
        if (order.Channel == SaleChannel.Pos)
        {
            return Field("channel", "Venda do PDV entra pela ingestão do caixa, não como pedido.");
        }

        if (order.SourceId == Guid.Empty)
        {
            return Field("sourceId", "Informe o pedido de origem.");
        }

        if (ValidatePayments(request.Payments.Select(p => (p.Id, p.Amount, p.ChangeAmount, p.Installments, Texts(p.Provider, p.CardBrand, p.Nsu, p.AuthorizationCode, p.PartLabel))).ToList()) is { } invalid)
        {
            return invalid;
        }

        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == order.SaleId, cancellationToken);
        var isNew = sale is null;
        var lines = new List<SaleLine>();
        if (sale is null)
        {
            if (ValidateHeader(order.SaleId, order.Number, order.SourceType, order.Lines, order.ServiceFee) is { } header)
            {
                return header;
            }

            var built = await BuildSaleAsync(order.SaleId, order.Channel, order.Number, order.SourceType, order.SourceId, order.StoreId, null, null, order.StockLocationId,
                order.CustomerId, order.SellerUserId, false, null, null, order.Lines, order.ServiceFee, cancellationToken);
            if (built.IsFailure)
            {
                return built.Error!;
            }

            (sale, lines) = built.Value;
        }
        else if (sale.Status == SaleStatus.Cancelled)
        {
            return Error.Conflict("sale_cancelled", "A venda deste pedido foi cancelada.");
        }

        var parts = isNew ? [] : await db.SaleParts.Where(p => p.SaleId == sale.Id).ToListAsync(cancellationToken);
        var existing = isNew ? [] : await db.SalePayments.Where(p => p.SaleId == sale.Id).ToListAsync(cancellationToken);
        var fresh = request.Payments.Where(p => existing.All(e => e.Id != p.Id)).ToList();
        if (fresh.Count > 0 && sale.Status == SaleStatus.Completed)
        {
            return Error.Conflict("sale_already_paid", "A venda deste pedido já está paga.");
        }

        var methods = await MethodsAsync(cancellationToken);
        var captured = SaleRules.CapturedNet(existing.Select(p => (p.Status, p.Amount, p.ChangeAmount)));
        if (SaleRules.Money(captured + fresh.Sum(p => p.Amount - p.ChangeAmount)) > sale.Total)
        {
            return Error.Validation("payment_exceeds_total", $"Os pagamentos passam do total da venda ({Money(sale.Total)}).");
        }

        var now = clock.GetUtcNow();
        var added = new List<SalePayment>();
        var newParts = new List<SalePart>();
        foreach (var input in fresh)
        {
            if (CheckMethod(methods, input.PaymentMethodId, input.ChangeAmount, sale.CustomerId, fromDevice: false) is { } methodError)
            {
                return methodError;
            }

            if (providers.FirstOrDefault(p => p.Integration == input.Integration) is not { } provider)
            {
                return Field("payments", "Integração de pagamento ainda não disponível: " + input.Integration + ".");
            }

            var label = string.IsNullOrWhiteSpace(input.PartLabel) ? DefaultPartLabel : input.PartLabel.Trim();
            var part = parts.Concat(newParts).FirstOrDefault(p => p.Label == label);
            if (part is null)
            {
                part = SalePart.Create(Guid.CreateVersion7(), sale.Id, label, label == DefaultPartLabel ? SalePartKind.Equal : SalePartKind.Amount,
                    SaleRules.Money(fresh.Where(p => (string.IsNullOrWhiteSpace(p.PartLabel) ? DefaultPartLabel : p.PartLabel.Trim()) == label).Sum(p => p.Amount - p.ChangeAmount)));
                newParts.Add(part);
            }

            var payment = SalePayment.Create(input.Id, sale.Id, part.Id, input.PaymentMethodId, SaleRules.Money(input.Amount), SaleRules.Money(input.ChangeAmount), input.Integration,
                input.Installments, Clean(input.Provider), input.AcquirerPartyId, Clean(input.CardBrand), Clean(input.Nsu), Clean(input.AuthorizationCode), Clean(input.Txid),
                Clean(input.EndToEndId), now);
            var outcome = await provider.CaptureAsync(input, cancellationToken);
            if (!payment.MoveTo(outcome.Status, currentUser.UserId, now))
            {
                return Field("payments", "O provedor devolveu uma situação inválida: " + outcome.Status + ".");
            }

            added.Add(payment);
        }

        var all = existing.Concat(added).ToList();
        captured = SaleRules.CapturedNet(all.Select(p => (p.Status, p.Amount, p.ChangeAmount)));
        var completed = sale.ApplyPayments(captured, now, BusinessDateOf(now));
        foreach (var part in parts.Concat(newParts))
        {
            var mine = all.Where(p => p.SalePartId == part.Id).ToList();
            part.Paid = completed || (mine.Count > 0 && mine.All(p => p.Status == SalePaymentStatus.Captured));
        }

        if (isNew)
        {
            if (await SaveNewAsync(sale, lines, newParts, added, cancellationToken) is { } saveError)
            {
                return saveError;
            }
        }
        else
        {
            db.SaleParts.AddRange(newParts);
            db.SalePayments.AddRange(added);
            if (completed)
            {
                PublishCompleted(sale);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return await ViewAsync(sale.Id, cancellationToken) is { } view ? view : Error.NotFound(Error.Codes.NotFound, "Venda não encontrada.");
    }

    /// <summary>
    /// Cancelamento (rascunho C §7, auditado): pagamento capturado só sai estornado; no manual, quem cancela confirma que estornou na maquininha.
    /// Os demais pagamentos (pendente, desconhecido) são cancelados. Publica <see cref="SaleCancelled"/>. Cancelar de novo devolve a venda.
    /// </summary>
    public async Task<Result<SaleView>> CancelAsync(SaleCancellationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > 200 || SaleRules.LooksLikeCardNumber(reason))
        {
            return Field("reason", "Informe o motivo do cancelamento (de 3 a 200 caracteres).");
        }

        var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == request.SaleId, cancellationToken);
        if (sale is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Venda não encontrada.");
        }

        if (sale.Status != SaleStatus.Cancelled)
        {
            var payments = await db.SalePayments.Where(p => p.SaleId == sale.Id).ToListAsync(cancellationToken);
            if (payments.Any(p => p.Status == SalePaymentStatus.Captured) && !request.CapturedRefundedOutside)
            {
                return Error.Validation("refund_required", "A venda tem pagamento capturado: estorne cada um na maquininha ou no provedor e confirme o estorno.");
            }

            var now = clock.GetUtcNow();
            foreach (var payment in payments.Where(p => !PaymentStateMachine.IsFinal(p.Status)))
            {
                payment.MoveTo(payment.Status == SalePaymentStatus.Captured ? SalePaymentStatus.Refunded : SalePaymentStatus.Voided, request.CancelledBy ?? currentUser.UserId, now);
            }

            sale.Cancel(reason, request.CancelledBy ?? currentUser.UserId, now);
            events.Publish(new SaleCancelled(sale.Id, sale.Number, sale.Channel, sale.StoreId, sale.CashSessionId, sale.BusinessDate, sale.Total, now));
            await db.SaveChangesAsync(cancellationToken);
        }

        return await ViewAsync(sale.Id, cancellationToken) is { } view ? view : Error.NotFound(Error.Codes.NotFound, "Venda não encontrada.");
    }

    /// <summary>A venda com linhas, partes e pagamentos (o custo vai junto; a rota tira o custo de quem não tem <c>sales.view_cost</c>).</summary>
    public async Task<SaleView?> ViewAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        var sale = await db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == saleId, cancellationToken);
        if (sale is null)
        {
            return null;
        }

        var lines = await db.SaleLines.AsNoTracking().Where(l => l.SaleId == saleId).OrderBy(l => l.LineNumber).ToListAsync(cancellationToken);
        var parts = await db.SaleParts.AsNoTracking().Where(p => p.SaleId == saleId).OrderBy(p => p.Label).ToListAsync(cancellationToken);
        var payments = await db.SalePayments.AsNoTracking().Where(p => p.SaleId == saleId).OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).ToListAsync(cancellationToken);
        return new SaleView(
            sale.Id, sale.Number, sale.Channel, sale.Status, sale.StockStatus, sale.StoreId, sale.RegisterId, sale.CashSessionId, sale.StockLocationId, sale.CustomerId, sale.SourceType,
            sale.SourceId, sale.BusinessDate, sale.CompletedAt, sale.Subtotal, sale.DiscountTotal, sale.ServiceFee, sale.Total,
            SaleRules.CapturedNet(payments.Select(p => (p.Status, p.Amount, p.ChangeAmount))),
            lines.Select(l => new SaleLineView(l.Id, l.LineNumber, l.ProductId, l.ParentProductId, l.ItemId, l.Description, l.UnitCode, l.Quantity, l.UnitPrice, l.Discount, l.Total, l.UnitCost,
                l.Note, l.Seat, l.ListUnitPrice, l.DiscountReason, l.PriceReason)).ToList(),
            parts.Select(p => new SalePartView(p.Id, p.Label, p.Kind, p.Amount, p.Paid)).ToList(),
            payments.Select(p => new SalePaymentView(p.Id, p.SalePartId, p.PaymentMethodId, p.Amount, p.ChangeAmount, p.Status, p.Integration, p.Installments, p.Provider,
                p.AcquirerPartyId, p.CardBrand, p.Nsu, p.AuthorizationCode, p.Txid, p.EndToEndId, p.ConfirmedBy, p.ConfirmedAt)).ToList(),
            sale.CancelledAt, sale.CancelReason);
    }
}
