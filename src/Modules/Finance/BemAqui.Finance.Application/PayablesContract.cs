using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// <see cref="IPayables"/> (FIN-02 CA 14 e 15): o título entra no DbContext do chamador, que grava na própria transação. A
/// conta vem da origem (instalador: "2.3 Instalação terceirizada"; demais: "3.9 Despesas a classificar").
/// </summary>
public sealed class PayablesContract(IFinanceDb db, IChartOfAccounts chart, INumberSequence numbers, ICurrentUser currentUser, TimeProvider clock) : IPayables
{
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
            return Error.Conflict("payable_account_missing", "Cadastre uma conta de despesa analítica ativa para os títulos gerados.");
        }

        var payable = new Payable
        {
            Number = await numbers.NextAsync(PayableService.DocType, cancellationToken), PartyId = partyId, Description = description.Trim(), AccountId = account.Id, ProjectId = projectId,
            CostType = projectId is null ? null : origin == DocumentOrigin.Installer ? CostType.Installation : CostType.Other,
            Amount = amount, IssueDate = BemAqui.BuildingBlocks.Time.BusinessTime.Today(clock), DueDate = dueDate, IsEstimate = planned && estimatedDueDate,
            Status = planned ? PayableStatus.Planned : PayableStatus.Open, Origin = origin, OriginId = originId, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.Payables.Add(payable);
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
            payable.Status = PayableStatus.Cancelled;
            payable.CancelReason = "Origem cancelada: " + reason.Trim();
            payable.CancelledAt = clock.GetUtcNow();
            payable.CancelledBy = currentUser.UserId;
        }

        return Result.Success();
    }

    public async Task<IReadOnlyList<ProjectPayable>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await db.Payables.AsNoTracking().Where(p => p.ProjectId == projectId).OrderBy(p => p.DueDate == null).ThenBy(p => p.DueDate).ThenBy(p => p.Number)
            .Select(p => new ProjectPayable(p.Id, p.Number, p.PartyId, p.Origin, p.CostType, p.DueDate, p.Amount, p.Amount - p.RetentionIss - p.RetentionInss - p.RetentionIr - p.RetentionPcc - p.PaidAmount, p.Status, p.CreatedAt))
            .ToListAsync(cancellationToken);

    private async Task<Payable?> FindAsync(DocumentOrigin origin, Guid originId, CancellationToken ct) =>
        db.Payables.Local.FirstOrDefault(p => p.Origin == origin && p.OriginId == originId)
        ?? await db.Payables.FirstOrDefaultAsync(p => p.Origin == origin && p.OriginId == originId, ct);

    private static PayableRef Ref(Payable p) => new(p.Id, p.Number, p.Status, p.Amount, p.DueDate, p.IsEstimate);
}
