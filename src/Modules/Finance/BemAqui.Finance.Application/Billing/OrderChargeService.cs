using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Cobrança do pedido de venda (VND-02a, <see cref="IOrderCharges"/>): o título <c>SalesOrder</c> contra o cliente (um ativo por pedido, índice único
/// parcial <see cref="ActiveIndex"/>) e o boleto com Pix pelo mesmo fluxo da BNK-01b (<see cref="BillingSlipService.IssueAsync"/>: validações, conta do
/// Inter, pré-condições do pagador, job de emissão). O título fica fora da régua de cobrança e da receita por título; a receita entra pela venda.
/// </summary>
public sealed class OrderChargeService(
    IFinanceDb db,
    BillingSlipService billing,
    BillingSettingsService settings,
    BillingSlipReactions reactions,
    BankPaymentService bankPayments,
    IBankBillingProvider bank,
    SaleReceivableWriter writer,
    IParties parties,
    INumberSequence numbers,
    ICurrentUser currentUser,
    TimeProvider clock) : IOrderCharges
{
    public const string ActiveIndex = "ux_receivables_sales_order_active";

    private DateOnly Today => BusinessTime.Today(clock);

    public async Task<OrderChargeReadiness> ReadinessAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var party = await parties.FindAsync(customerId, cancellationToken);
        var address = await parties.DefaultAddressAsync(customerId, AddressKind.Billing, cancellationToken);
        var contact = await parties.BillingContactAsync(customerId, cancellationToken);
        var hasDocument = !string.IsNullOrWhiteSpace(party?.Document);
        var hasAddress = BillingSlipService.AddressComplete(address);
        var missing = new List<string>();
        if (!hasDocument)
        {
            missing.Add("CPF ou CNPJ do cliente");
        }

        if (!hasAddress)
        {
            missing.Add("Endereço de cobrança completo (CEP, rua, número, bairro, cidade e UF)");
        }

        var email = contact?.Email is { Length: > 0 } e && BemAqui.BuildingBlocks.Email.EmailAddresses.IsPlain(e.Trim()) ? e.Trim() : null;
        return new OrderChargeReadiness(await AccountAsync(cancellationToken) is not null, hasDocument, hasAddress, email, missing);
    }

    public async Task<Result<OrderChargeView>> ChargeAsync(OrderChargeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0)
        {
            return ReceivableService.Invalid("amount", "O pedido precisa ter valor para gerar cobrança.");
        }

        if (await AccountAsync(cancellationToken) is not { } account)
        {
            return Error.Conflict(BillingSlipService.MissingIntegrationCode, BillingSlipService.NoIntegrationMessage);
        }

        var receivable = await ActiveAsync(request.OrderId, cancellationToken);
        if (receivable is not null && await reactions.OpenSlipAsync(receivable, cancellationToken) is not null)
        {
            // Mesmo pedido de novo (clique duplo, nova tentativa): devolve a cobrança que já está em andamento.
            return (await FindAsync(request.OrderId, cancellationToken))!;
        }

        if (receivable is null)
        {
            if (await writer.RevenueAccountAsync(cancellationToken) is not { } revenue)
            {
                return Error.Validation("sales_revenue_account_missing", "Cadastre uma conta de receita no plano de contas para cobrar pedidos.");
            }

            receivable = new Receivable
            {
                Number = await numbers.NextAsync(ReceivableService.DocType, cancellationToken), PartyId = request.CustomerId, Description = "Pedido de venda " + request.OrderNumber,
                AccountId = revenue, IssueDate = Today, DueDate = request.DueDate, Amount = request.Amount, Status = ReceivableStatus.Open, Origin = DocumentOrigin.SalesOrder,
                OriginId = request.OrderId, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
            };
            db.Receivables.Add(receivable);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, ActiveIndex))
            {
                // Outra cobrança do mesmo pedido gravou primeiro: devolve a dela.
                db.Entry(receivable).State = EntityState.Detached;
                return await FindAsync(request.OrderId, cancellationToken) is { } other ? other : Error.Conflict(BillingSlipService.ExistsCode, "Este pedido já tem cobrança.");
            }
        }
        else if (receivable.PaidAmount == 0 && (receivable.Amount != request.Amount || receivable.DueDate != request.DueDate))
        {
            // O pedido voltou a Aberto (cobrança vencida ou cancelada) e foi alterado: o título acompanha o novo total e vencimento.
            receivable.Amount = request.Amount;
            receivable.DueDate = request.DueDate;
            receivable.CancelReason = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        var defaults = await settings.DefaultsAsync(cancellationToken);
        var issued = await billing.IssueAsync(receivable.Id, new BillingSlipInput(account, request.DueDate, defaults.FinePct, defaults.InterestMonthPct, 0m, null, null, request.SendEmail,
            receivable.Version), request.ClientRequestId, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error!;
        }

        return (await FindAsync(request.OrderId, cancellationToken))!;
    }

    public async Task<OrderChargeView?> FindAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var receivable = await db.Receivables.AsNoTracking().Where(r => r.Origin == DocumentOrigin.SalesOrder && r.OriginId == orderId)
            .OrderBy(r => r.Status == ReceivableStatus.Cancelled ? 1 : 0).ThenByDescending(r => r.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (receivable is null)
        {
            return null;
        }

        var slip = await db.BillingSlips.AsNoTracking().Where(s => s.ReceivableId == receivable.Id).OrderByDescending(s => s.RequestedAt).FirstOrDefaultAsync(cancellationToken);
        var settledOn = await db.ReceivableSettlements.AsNoTracking().Where(s => s.ReceivableId == receivable.Id && s.ReversedAt == null).OrderByDescending(s => s.Date)
            .Select(s => (DateOnly?)s.Date).FirstOrDefaultAsync(cancellationToken);
        return new OrderChargeView(receivable.Id, receivable.Number, receivable.Status, receivable.Amount, receivable.PaidAmount, receivable.DueDate, settledOn,
            slip is null ? null : new OrderSlipView(slip.Id, Map(slip.Status), slip.Amount, slip.DueDate, slip.DigitableLine, slip.PixCopyPaste, slip.QrCode, slip.PdfAttachmentId is not null,
                slip.FailureReason, slip.CancelPending, slip.RequestedAt, slip.IssuedAt, slip.PaidAt),
            receivable.RefundedAt);
    }

    public async Task<Result<OrderSlipRecheck>> RecheckSlipAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var receivable = await ActiveAsync(orderId, cancellationToken);
        if (receivable is null)
        {
            return new OrderSlipRecheck(false, null);
        }

        if (receivable.Status == ReceivableStatus.Paid)
        {
            var on = await db.ReceivableSettlements.AsNoTracking().Where(s => s.ReceivableId == receivable.Id && s.ReversedAt == null).Select(s => (DateOnly?)s.Date).MaxAsync(cancellationToken);
            return new OrderSlipRecheck(true, on);
        }

        var slip = await db.BillingSlips.AsNoTracking().Where(s => s.ReceivableId == receivable.Id).OrderByDescending(s => s.RequestedAt).FirstOrDefaultAsync(cancellationToken);
        if (slip?.Status == BillingSlipStatus.PaidPendingReview || slip?.Status == BillingSlipStatus.Paid)
        {
            return new OrderSlipRecheck(true, slip.CreditDate ?? (slip.PaidAt is { } at ? DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime) : Today));
        }

        if (slip is not { Status: BillingSlipStatus.Issued, ProviderRequestId: { } requestId })
        {
            return new OrderSlipRecheck(false, null);
        }

        var integration = await db.BankIntegrations.AsNoTracking().FirstOrDefaultAsync(i => i.BankAccountId == slip.BankAccountId && i.Status == BankIntegrationStatus.Active, cancellationToken);
        if (integration is null)
        {
            return Error.Conflict(BillingSlipService.MissingIntegrationCode, "Não foi possível consultar o boleto no Inter: a integração está desligada. Tente de novo depois.");
        }

        var read = await bank.GetAsync(integration, requestId, cancellationToken);
        if (!read.Ok)
        {
            return Error.Conflict("bank_unavailable", "Não foi possível consultar o boleto no Inter agora. Tente de novo em alguns minutos.");
        }

        if (read.Value!.Situation != BankSlipSituation.Paid)
        {
            return new OrderSlipRecheck(false, null);
        }

        // Pago lá: aplica a baixa já (o mesmo caminho do aviso e da conferência diária); o pedido fica pago pela baixa do banco.
        await bankPayments.ApplyAsync(slip.Id, read.Value, BankPaymentOrigin.Reconcile, cancellationToken);
        return new OrderSlipRecheck(true, read.Value.PaidOn ?? Today);
    }

    public async Task CancelAsync(Guid orderId, string reason, bool paidOtherMeans = false, CancellationToken cancellationToken = default)
    {
        var receivable = await ActiveAsync(orderId, cancellationToken);
        if (receivable is null || !receivable.IsOpen)
        {
            return;
        }

        var text = reason.Length <= 200 ? reason : reason[..200];
        if (await reactions.OpenSlipAsync(receivable, cancellationToken) is not null)
        {
            // O título é cancelado quando o banco confirma o cancelamento do boleto (BillingSlipProcessor); o pagamento que chegar antes segue a baixa.
            receivable.CancelReason = text;
            await reactions.RequestCancelAsync(receivable, paidOtherMeans ? BillingSlipCancelReason.PaidOtherMeans : BillingSlipCancelReason.CustomerRequest, cancellationToken);
            return;
        }

        if (receivable.PaidAmount == 0)
        {
            receivable.Status = ReceivableStatus.Cancelled;
            receivable.CancelReason = text;
            receivable.CancelledAt = clock.GetUtcNow();
            receivable.CancelledBy = currentUser.UserId;
        }
    }

    public async Task<Result> RefundAsync(Guid orderId, string reason, CancellationToken cancellationToken = default)
    {
        var receivable = await db.Receivables.Where(r => r.Origin == DocumentOrigin.SalesOrder && r.OriginId == orderId && r.PaidAmount > 0)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (receivable is null)
        {
            return Error.Conflict("order_charge_not_paid", "Este pedido não tem pagamento por boleto para devolver.");
        }

        if (receivable.RefundedAt is not null)
        {
            return Result.Success();
        }

        var settlements = await db.ReceivableSettlements.AsNoTracking().Where(s => s.ReceivableId == receivable.Id && s.ReversedAt == null).ToListAsync(cancellationToken);
        var account = settlements.Select(s => s.BankAccountId).FirstOrDefault(a => a is not null)
            ?? await db.FinancialTransactions.AsNoTracking().Where(t => t.ReceivableId == receivable.Id && t.Amount > 0).Select(t => (Guid?)t.BankAccountId).FirstOrDefaultAsync(cancellationToken);
        if (account is null)
        {
            return Error.Conflict("order_charge_no_account", "Não foi possível achar a conta do recebimento deste pedido.");
        }

        var now = clock.GetUtcNow();
        db.FinancialTransactions.Add(new FinancialTransaction
        {
            BankAccountId = account.Value, Date = Today, Amount = -settlements.Sum(s => s.Total), Type = TransactionType.Receipt,
            Description = Cut("Devolução ao cliente · " + receivable.Number + " · " + reason, 200), ReceivableId = receivable.Id, CreatedAt = now, CreatedBy = currentUser.UserId,
        });
        receivable.RefundedAt = now;
        receivable.RefundedBy = currentUser.UserId;
        return Result.Success();
    }

    /// <summary>O título não cancelado do pedido (no máximo um, pelo índice).</summary>
    private Task<Receivable?> ActiveAsync(Guid orderId, CancellationToken ct) =>
        db.Receivables.FirstOrDefaultAsync(r => r.Origin == DocumentOrigin.SalesOrder && r.OriginId == orderId && r.Status != ReceivableStatus.Cancelled, ct);

    /// <summary>A conta com a integração Inter ativa e testada (a padrão primeiro), como no diálogo "Emitir boleto".</summary>
    private async Task<Guid?> AccountAsync(CancellationToken ct)
    {
        var rows = await db.BankIntegrations.AsNoTracking().Where(i => i.Status == BankIntegrationStatus.Active && i.LastTestOutcome == BankTestOutcome.Ok)
            .Join(db.BankAccounts.AsNoTracking().Where(a => a.IsActive), i => i.BankAccountId, a => a.Id, (i, a) => new { a.Id, a.Name, a.IsDefault }).ToListAsync(ct);
        return rows.OrderByDescending(r => r.IsDefault).ThenBy(r => r.Name, StringComparer.CurrentCulture).Select(r => (Guid?)r.Id).FirstOrDefault();
    }

    private static OrderSlipStatus Map(BillingSlipStatus status) => status switch
    {
        BillingSlipStatus.Requested => OrderSlipStatus.Requested,
        BillingSlipStatus.Issued => OrderSlipStatus.Issued,
        BillingSlipStatus.Failed => OrderSlipStatus.Failed,
        BillingSlipStatus.Paid => OrderSlipStatus.Paid,
        BillingSlipStatus.PaidPendingReview => OrderSlipStatus.PaidPendingReview,
        BillingSlipStatus.Cancelled => OrderSlipStatus.Cancelled,
        _ => OrderSlipStatus.Expired,
    };

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}
