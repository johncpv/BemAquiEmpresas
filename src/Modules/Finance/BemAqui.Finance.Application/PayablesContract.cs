using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// <see cref="IPayables"/> (FIN-02 CA 14 e 15; ADR 0024 §2): o título entra no DbContext do chamador, que grava na própria transação.
/// Nos métodos de uma parcela (<c>installment = 1</c>) a conta vem da origem (instalador: "2.3 Instalação terceirizada"; demais: "3.9
/// Despesas a classificar"); nos métodos por parcela, conta e natureza vêm do chamador. O índice único parcial
/// <c>(origin, origin_id, installment) WHERE status &lt;&gt; 'Cancelled'</c> segura a idempotência em paralelo.
/// </summary>
public sealed class PayablesContract(IFinanceDb db, IChartOfAccounts chart, INumberSequence numbers, ICurrentUser currentUser, TimeProvider clock) : IPayables
{
    public const string PaidCode = "payable_paid";
    public const int MaxInstallments = 60;

    public async Task<Result<PayableRef>> CreateFromOriginAsync(
        DocumentOrigin origin, Guid originId, Guid partyId, Guid? projectId, decimal amount, DateOnly? dueDate, bool estimatedDueDate, string description, bool planned,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        var existing = await FindAsync(origin, originId, cancellationToken);
        if (existing is not null)
        {
            return Ref(existing);
        }

        if (amount <= 0)
        {
            return ReceivableService.Invalid("amount", "Informe um valor maior que zero.");
        }

        if (!planned && dueDate is null)
        {
            return ReceivableService.Invalid("dueDate", "Informe o vencimento.");
        }

        var code = origin == DocumentOrigin.Installer ? "2.3" : "3.9";
        var accounts = await chart.PostableAsync(AccountType.Expense, cancellationToken);
        var account = accounts.FirstOrDefault(a => a.Code == code) ?? accounts.FirstOrDefault(a => a.Code == "3.9");
        if (account is null)
        {
            return AccountMissing();
        }

        var payable = await NewAsync(origin, originId, 1, partyId, projectId, account.Id,
            projectId is null ? null : origin == DocumentOrigin.Installer ? CostType.Installation : CostType.Other, amount, dueDate, planned && estimatedDueDate, description,
            planned ? PayableStatus.Planned : PayableStatus.Open, cancellationToken);
        return Ref(payable);
    }

    public async Task<Result<PayableRef>> ConfirmAsync(DocumentOrigin origin, Guid originId, decimal amount, DateOnly dueDate, CancellationToken cancellationToken = default)
    {
        var payable = await FindAsync(origin, originId, cancellationToken);
        if (payable is null)
        {
            return PayableService.NotFound();
        }

        if (payable.InPaymentOrder)
        {
            return PayableService.InOrder(payable);
        }

        if (payable.Status is not (PayableStatus.Planned or PayableStatus.Open))
        {
            return Error.Conflict("payable_not_open", $"O título {payable.Number} não está previsto nem em aberto.");
        }

        payable.Amount = amount;
        payable.DueDate = dueDate;
        payable.IsEstimate = false;
        payable.Status = PayableStatus.Open;
        return Ref(payable);
    }

    public async Task<Result<PayableRef>> UpdatePlannedAsync(DocumentOrigin origin, Guid originId, decimal amount, DateOnly? dueDate, CancellationToken cancellationToken = default)
    {
        var payable = await FindAsync(origin, originId, cancellationToken);
        if (payable is null)
        {
            return PayableService.NotFound();
        }

        if (payable.Status != PayableStatus.Planned)
        {
            return payable.InPaymentOrder ? PayableService.InOrder(payable) : Error.Conflict("payable_not_planned", $"O título {payable.Number} já não está previsto.");
        }

        payable.Amount = amount;
        payable.DueDate = dueDate ?? payable.DueDate;
        return Ref(payable);
    }

    public async Task<Result> CancelFromOriginAsync(DocumentOrigin origin, Guid originId, string reason, CancellationToken cancellationToken = default)
    {
        var payable = await FindAsync(origin, originId, cancellationToken);
        if (payable is null)
        {
            return PayableService.NotFound();
        }

        if (payable.InPaymentOrder)
        {
            return PayableService.InOrder(payable);
        }

        if (payable.Status is PayableStatus.Planned or PayableStatus.Open)
        {
            Cancel(payable, reason);
        }

        return Result.Success();
    }

    public async Task<PayableRef?> FindByOriginAsync(DocumentOrigin origin, Guid originId, CancellationToken cancellationToken = default) =>
        await FindAsync(origin, originId, cancellationToken) is { } payable ? Ref(payable) : null;

    public async Task<Result<IReadOnlyList<PayableRef>>> CreatePlannedInstallmentsAsync(DocumentOrigin origin, Guid originId, PlannedPayables plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (await PlanErrorAsync(plan, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var current = await ActiveOfAsync(origin, originId, cancellationToken);
        var result = new List<PayableRef>();
        foreach (var installment in plan.Installments)
        {
            var existing = current.FirstOrDefault(p => p.Installment == installment.Number);
            result.Add(Ref(existing ?? await NewPlannedAsync(origin, originId, plan, installment, cancellationToken)));
        }

        return result;
    }

    public async Task<Result<IReadOnlyList<PayableRef>>> ReplacePlannedAsync(DocumentOrigin origin, Guid originId, PlannedPayables plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (await PlanErrorAsync(plan, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var current = await ActiveOfAsync(origin, originId, cancellationToken);
        if (await LockedErrorAsync(current, cancellationToken) is { } locked)
        {
            return locked;
        }

        var planned = current.Where(p => p.Status == PayableStatus.Planned).ToList();
        var confirmed = current.Where(p => p.Status != PayableStatus.Planned).Select(p => p.Installment).ToHashSet();
        var result = new List<PayableRef>();
        foreach (var installment in plan.Installments)
        {
            if (confirmed.Contains(installment.Number))
            {
                return Error.Conflict("payable_not_planned", $"A parcela {installment.Number} já foi confirmada e não volta a ser prevista.");
            }

            if (planned.FirstOrDefault(p => p.Installment == installment.Number) is { } same)
            {
                // A parcela que já existe é atualizada no lugar: nunca há dois títulos ativos na mesma parcela (índice único parcial).
                same.PartyId = plan.PartyId;
                same.ProjectId = plan.ProjectId;
                same.AccountId = plan.AccountId;
                same.CostType = CostOf(plan);
                same.Amount = installment.Amount;
                same.DueDate = installment.EstimatedDueDate;
                same.IsEstimate = true;
                same.Description = Description(installment);
                result.Add(Ref(same));
            }
            else
            {
                result.Add(Ref(await NewPlannedAsync(origin, originId, plan, installment, cancellationToken)));
            }
        }

        var numbersKept = plan.Installments.Select(i => i.Number).ToHashSet();
        foreach (var extra in planned.Where(p => !numbersKept.Contains(p.Installment)))
        {
            Cancel(extra, "parcela substituída");
        }

        return result;
    }

    public async Task<Result> CancelAllFromOriginAsync(DocumentOrigin origin, Guid originId, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var current = await ActiveOfAsync(origin, originId, cancellationToken);
        if (await LockedErrorAsync(current, cancellationToken) is { } locked)
        {
            return locked;
        }

        foreach (var payable in current.Where(p => p.Status is PayableStatus.Planned or PayableStatus.Open))
        {
            Cancel(payable, reason);
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<PayableRef>>> CreateOpenInstallmentsAsync(DocumentOrigin origin, Guid originId, OpenPayables plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var asPlanned = new PlannedPayables(plan.PartyId, plan.ProjectId, plan.AccountId, plan.CostType,
            (plan.Installments ?? []).Select(i => new PlannedInstallment(i.Number, i.Amount, i.DueDate, i.Description)).ToList());
        if (await PlanErrorAsync(asPlanned, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var current = await ActiveOfAsync(origin, originId, cancellationToken);
        var result = new List<PayableRef>();
        foreach (var installment in plan.Installments!)
        {
            var existing = current.FirstOrDefault(p => p.Installment == installment.Number);
            result.Add(Ref(existing ?? await NewAsync(origin, originId, installment.Number, plan.PartyId, plan.ProjectId, plan.AccountId, CostOf(asPlanned), installment.Amount,
                installment.DueDate, false, Description(new PlannedInstallment(installment.Number, installment.Amount, installment.DueDate, installment.Description)), PayableStatus.Open,
                cancellationToken)));
        }

        return result;
    }

    public async Task<IReadOnlyList<PayableRef>> ListByOriginAsync(DocumentOrigin origin, Guid originId, CancellationToken cancellationToken = default)
    {
        var payables = await db.Payables.AsNoTracking().Where(p => p.Origin == origin && p.OriginId == originId)
            .OrderBy(p => p.Installment).ThenBy(p => p.Status == PayableStatus.Cancelled).ThenBy(p => p.Number).ToListAsync(cancellationToken);
        var inOrder = payables.Where(p => p.InPaymentOrder).Select(p => p.Id).ToList();
        var orders = inOrder.Count == 0 ? [] : await db.PaymentOrderItems.AsNoTracking().Where(i => inOrder.Contains(i.PayableId))
            .Join(db.PaymentOrders.AsNoTracking(), i => i.PaymentOrderId, o => o.Id, (i, o) => new { i.PayableId, o.Id, o.Status })
            .Where(x => x.Status == PaymentOrderStatus.Pending || x.Status == PaymentOrderStatus.Approved).ToListAsync(cancellationToken);
        return payables.Select(p => Ref(p) with { PaymentOrderId = orders.FirstOrDefault(o => o.PayableId == p.Id)?.Id }).ToList();
    }

    public async Task<IReadOnlyList<ProjectPayable>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await db.Payables.AsNoTracking().Where(p => p.ProjectId == projectId).OrderBy(p => p.DueDate == null).ThenBy(p => p.DueDate).ThenBy(p => p.Number)
            .Select(p => new ProjectPayable(p.Id, p.Number, p.PartyId, p.Origin, p.CostType, p.DueDate, p.Amount, p.Amount - p.RetentionIss - p.RetentionInss - p.RetentionIr - p.RetentionPcc - p.PaidAmount, p.Status, p.CreatedAt))
            .ToListAsync(cancellationToken);

    /// <summary>Parcelas válidas (1 a 60, números únicos e positivos, valor &gt; 0, descrição) e conta analítica ativa.</summary>
    private async Task<Error?> PlanErrorAsync(PlannedPayables plan, CancellationToken ct)
    {
        if (plan.Installments is not { Count: > 0 and <= MaxInstallments })
        {
            return ReceivableService.Invalid("installments", $"Informe de 1 a {MaxInstallments} parcelas.");
        }

        if (plan.Installments.Any(i => i.Number < 1) || plan.Installments.Select(i => i.Number).Distinct().Count() != plan.Installments.Count)
        {
            return ReceivableService.Invalid("installments", "Cada parcela tem um número único, a partir de 1.");
        }

        if (plan.Installments.Any(i => i.Amount <= 0 || decimal.Round(i.Amount, 2) != i.Amount))
        {
            return ReceivableService.Invalid("installments", "Cada parcela precisa de um valor maior que zero, em centavos.");
        }

        if (plan.Installments.Any(i => string.IsNullOrWhiteSpace(i.Description)))
        {
            return ReceivableService.Invalid("installments", "Cada parcela precisa da descrição do título.");
        }

        var found = await chart.FindAsync([plan.AccountId], ct);
        return found.Count > 0 && found[0] is { AcceptsEntries: true, IsActive: true } ? null : AccountMissing();
    }

    /// <summary>Título da origem numa OP não finalizada ou já com pagamento: nada muda (409 com o link para a OP).</summary>
    private async Task<Error?> LockedErrorAsync(IReadOnlyList<Payable> current, CancellationToken ct)
    {
        if (current.FirstOrDefault(p => p.InPaymentOrder) is { } inOrder)
        {
            var orderId = await db.PaymentOrderItems.AsNoTracking().Where(i => i.PayableId == inOrder.Id)
                .Join(db.PaymentOrders.AsNoTracking(), i => i.PaymentOrderId, o => o.Id, (i, o) => new { o.Id, o.Status })
                .Where(o => o.Status == PaymentOrderStatus.Pending || o.Status == PaymentOrderStatus.Approved).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
            return PayableService.InOrder(inOrder) with { Data = new Dictionary<string, object?> { ["paymentOrderId"] = orderId, ["payableId"] = inOrder.Id } };
        }

        return current.FirstOrDefault(p => p.PaidAmount > 0 || p.Status is PayableStatus.PartiallyPaid or PayableStatus.Paid) is { } paid
            ? Error.Conflict(PaidCode, $"O título {paid.Number} já teve pagamento. Estorne o pagamento antes.")
            : null;
    }

    private async Task<List<Payable>> ActiveOfAsync(DocumentOrigin origin, Guid originId, CancellationToken ct)
    {
        var local = db.Payables.Local.Where(p => p.Origin == origin && p.OriginId == originId).ToList();
        var stored = await db.Payables.Where(p => p.Origin == origin && p.OriginId == originId && p.Status != PayableStatus.Cancelled).ToListAsync(ct);
        return stored.Union(local).Where(p => p.Status != PayableStatus.Cancelled).OrderBy(p => p.Installment).ToList();
    }

    private Task<Payable> NewPlannedAsync(DocumentOrigin origin, Guid originId, PlannedPayables plan, PlannedInstallment installment, CancellationToken ct) =>
        NewAsync(origin, originId, installment.Number, plan.PartyId, plan.ProjectId, plan.AccountId, CostOf(plan), installment.Amount, installment.EstimatedDueDate, true,
            Description(installment), PayableStatus.Planned, ct);

    private async Task<Payable> NewAsync(
        DocumentOrigin origin, Guid originId, short installment, Guid partyId, Guid? projectId, Guid accountId, CostType? costType, decimal amount, DateOnly? dueDate, bool estimate,
        string description, PayableStatus status, CancellationToken ct)
    {
        var payable = new Payable
        {
            Number = await numbers.NextAsync(PayableService.DocType, ct), PartyId = partyId, Description = description.Trim(), AccountId = accountId, ProjectId = projectId,
            CostType = costType, Amount = amount, IssueDate = BemAqui.BuildingBlocks.Time.BusinessTime.Today(clock), DueDate = dueDate, IsEstimate = estimate, Status = status,
            Origin = origin, OriginId = originId, Installment = installment, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.Payables.Add(payable);
        return payable;
    }

    /// <summary>Com projeto a natureza é obrigatória (COM-04a CA 18): sem natureza no plano, vai para Outros.</summary>
    private static CostType? CostOf(PlannedPayables plan) => plan.ProjectId is null ? plan.CostType : plan.CostType ?? CostType.Other;

    private static string Description(PlannedInstallment installment)
    {
        var text = installment.Description.Trim();
        return text.Length <= 200 ? text : text[..200];
    }

    private void Cancel(Payable payable, string reason)
    {
        payable.Status = PayableStatus.Cancelled;
        var text = "Origem cancelada: " + reason.Trim();
        payable.CancelReason = text.Length <= 200 ? text : text[..200];
        payable.CancelledAt = clock.GetUtcNow();
        payable.CancelledBy = currentUser.UserId;
    }

    /// <summary>
    /// O título da parcela 1 da origem (métodos de uma parcela): o ativo, se houver; senão o mais recente (cancelado), como antes do
    /// <c>installment</c>.
    /// </summary>
    private async Task<Payable?> FindAsync(DocumentOrigin origin, Guid originId, CancellationToken ct) =>
        db.Payables.Local.Where(p => p.Origin == origin && p.OriginId == originId && p.Installment == 1).OrderBy(p => p.Status == PayableStatus.Cancelled).FirstOrDefault()
        ?? await db.Payables.Where(p => p.Origin == origin && p.OriginId == originId && p.Installment == 1)
            .OrderBy(p => p.Status == PayableStatus.Cancelled).ThenByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

    private static Error AccountMissing() => Error.Conflict("payable_account_missing", "Cadastre uma conta de despesa analítica ativa para os títulos gerados.");

    private static PayableRef Ref(Payable p) => new(p.Id, p.Number, p.Status, p.Amount, p.DueDate, p.IsEstimate, p.Installment);
}
