using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Aditivos do contrato (COM-07): rascunho, envio, aprovação por quem não registrou e reprovação. Aprovado, soma no orçado e
/// na data prometida e publica <c>ContractAmended</c>; o contrato original nunca muda.
/// </summary>
public sealed partial class AmendmentService(
    ICommercialDb db, IReceivables receivables, IFinanceParameters financeParameters, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users,
    INotificationService notifications, IEventPublisher events, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    /// <summary>Valor vigente (CA 10): o original mais os aditivos aprovados.</summary>
    public async Task<decimal> CurrentValueAsync(Contract contract, CancellationToken ct) =>
        contract.TotalValue + await db.ContractAmendments.Where(a => a.ContractId == contract.Id && a.Status == AmendmentStatus.Approved).SumAsync(a => a.ValueDelta, ct);

    public async Task<IReadOnlyList<AmendmentSummary>> ListAsync(Guid contractId, CancellationToken cancellationToken = default) =>
        await db.ContractAmendments.AsNoTracking().Where(a => a.ContractId == contractId).OrderBy(a => a.Number)
            .Select(a => new AmendmentSummary(a.Id, a.Number, a.Description, a.ValueDelta, a.DaysDelta, a.Status, a.CreatedAt, a.ApprovedAt)).ToListAsync(cancellationToken);

    /// <summary>Ponto de partida da página (CA 1 a 3): valor vigente, data prometida, títulos reduzíveis e, com custo, o orçado.</summary>
    public async Task<Result<AmendmentContext>> ContextAsync(Guid contractId, CancellationToken cancellationToken = default)
    {
        var contract = await db.Contracts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contractId, cancellationToken);
        if (contract is null)
        {
            return ContractService.NotFound();
        }

        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contractId, cancellationToken);
        var budget = await CanCostAsync(cancellationToken) && project is not null ? await BudgetAsync(project.Id, cancellationToken) : null;
        return new AmendmentContext(await CurrentValueAsync(contract, cancellationToken), project?.PromisedDate, await ReducibleAsync(contractId, cancellationToken), budget);
    }

    public async Task<Result<AmendmentDetail>> GetAsync(Guid contractId, Guid id, CancellationToken cancellationToken = default)
    {
        var amendment = await db.ContractAmendments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && a.ContractId == contractId, cancellationToken);
        if (amendment is null)
        {
            return NotFound();
        }

        var contract = await db.Contracts.AsNoTracking().FirstAsync(c => c.Id == contractId, cancellationToken);
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.ContractId == contractId, cancellationToken);
        var milestones = await db.ContractAmendmentMilestones.AsNoTracking().Where(m => m.AmendmentId == id).OrderBy(m => m.Sequence)
            .Select(m => new AmendmentMilestoneView(m.Sequence, m.Description, m.Percent, m.Amount, m.Trigger, m.TriggerEvent, m.DueDate, m.DaysAfterEvent)).ToListAsync(cancellationToken);
        var parcels = (await receivables.ForContractAsync(contractId, cancellationToken)).ToDictionary(p => p.Id);
        var reductions = await db.ContractAmendmentReductions.AsNoTracking().Where(r => r.AmendmentId == id).ToListAsync(cancellationToken);
        var current = await CurrentValueAsync(contract, cancellationToken);
        var approved = amendment.Status == AmendmentStatus.Approved;
        var before = approved ? current - amendment.ValueDelta : current;
        var promisedBefore = approved ? project?.PromisedDate?.AddDays(-amendment.DaysDelta) : project?.PromisedDate;
        var isAuthor = amendment.CreatedBy is { } author && author == currentUser.UserId;
        var canDecide = amendment.Status == AmendmentStatus.PendingApproval && await permissions.HasPermissionAsync("projects", "approve", cancellationToken)
            && (await DecisionRuleAsync(amendment, cancellationToken)).Allowed;
        return new AmendmentDetail(
            amendment.Id, contractId, contract.Number, amendment.Number, amendment.Description, amendment.ValueDelta, amendment.DaysDelta, amendment.Status, milestones,
            reductions.Select(r => new AmendmentReductionView(r.ReceivableId, parcels.GetValueOrDefault(r.ReceivableId)?.Number, parcels.GetValueOrDefault(r.ReceivableId)?.Amount, r.NewAmount)).ToList(),
            new AmendmentPreview(before, before + amendment.ValueDelta, promisedBefore, promisedBefore?.AddDays(amendment.DaysDelta)),
            await CanCostAsync(cancellationToken) ? Deltas(amendment.BudgetDeltas) : null, isAuthor, canDecide, amendment.SelfApproved, amendment.RejectionComment, amendment.ApprovedAt,
            amendment.ApprovedBy is { } a ? await users.DisplayNameAsync(a, cancellationToken) : null, amendment.CreatedBy is { } c ? await users.DisplayNameAsync(c, cancellationToken) : null,
            amendment.Version);
    }

    /// <summary>Títulos sem baixa do contrato, do último para o primeiro (CA 3).</summary>
    private async Task<List<ReducibleReceivable>> ReducibleAsync(Guid contractId, CancellationToken ct) =>
        (await receivables.ForContractAsync(contractId, ct))
            .Where(r => r.Status is ReceivableStatus.Planned or ReceivableStatus.Open && !r.HasSettlement)
            .OrderByDescending(r => r.DueDate is null).ThenByDescending(r => r.DueDate).ThenByDescending(r => r.Number)
            .Select(r => new ReducibleReceivable(r.Id, r.Number, r.Description, r.Amount, r.DueDate)).ToList();

    private async Task<Dictionary<CostType, decimal>> BudgetAsync(Guid projectId, CancellationToken ct) =>
        await db.ProjectBudgets.AsNoTracking().Where(b => b.ProjectId == projectId).ToDictionaryAsync(b => b.CostType, b => b.BudgetedAmount, ct);

    private ValueTask<bool> CanCostAsync(CancellationToken ct) => permissions.HasPermissionAsync("projects", "view_cost", ct);

    private static Dictionary<CostType, decimal> Deltas(string json) => JsonSerializer.Deserialize<Dictionary<CostType, decimal>>(json, Json) ?? [];

    private static string Serialize(Dictionary<CostType, decimal> deltas) => JsonSerializer.Serialize(deltas, Json);

    internal static Error NotFound() => Error.NotFound("amendment_not_found", "Aditivo não encontrado.");

    private static Error Stale() => Error.Conflict("concurrency_conflict", "O aditivo mudou desde que você abriu. Recarregue.");
}
