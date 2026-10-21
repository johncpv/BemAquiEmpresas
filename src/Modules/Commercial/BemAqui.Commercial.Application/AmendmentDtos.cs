using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application;

public sealed record AmendmentMilestoneInput(string Description, decimal Percent, MilestoneTrigger Trigger, TriggerEvent? TriggerEvent, DateOnly? DueDate, int? DaysAfterEvent);

public sealed record AmendmentReductionInput(Guid ReceivableId, decimal NewAmount);

/// <summary>Rascunho do aditivo (COM-07 CA 1, 3, 4); a distribuição no orçado só vale com <c>projects.view_cost</c>.</summary>
public sealed record AmendmentInput(
    string Description, decimal ValueDelta, int DaysDelta, IReadOnlyList<AmendmentMilestoneInput>? Milestones, IReadOnlyList<AmendmentReductionInput>? Reductions,
    Dictionary<CostType, decimal>? BudgetDeltas, uint? Version);

public sealed record AmendmentSummary(Guid Id, int Number, string Description, decimal ValueDelta, int DaysDelta, AmendmentStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt);

public sealed record AmendmentMilestoneView(int Sequence, string Description, decimal Percent, decimal Amount, MilestoneTrigger Trigger, TriggerEvent? TriggerEvent, DateOnly? DueDate, int? DaysAfterEvent);

public sealed record AmendmentReductionView(Guid ReceivableId, string? Number, decimal? CurrentAmount, decimal NewAmount);

/// <summary>Prévia "antes → depois" (CA 2): contratado, data prometida e, com custo, o orçado.</summary>
public sealed record AmendmentPreview(decimal ValueBefore, decimal ValueAfter, DateOnly? PromisedBefore, DateOnly? PromisedAfter);

public sealed record AmendmentDetail(
    Guid Id, Guid ContractId, string ContractNumber, int Number, string Description, decimal ValueDelta, int DaysDelta, AmendmentStatus Status,
    IReadOnlyList<AmendmentMilestoneView> Milestones, IReadOnlyList<AmendmentReductionView> Reductions, AmendmentPreview Preview, Dictionary<CostType, decimal>? BudgetDeltas,
    bool IsAuthor, bool CanDecide, bool SelfApproved, string? RejectionComment, DateTimeOffset? ApprovedAt, string? ApprovedByName, string? CreatedByName, uint Version);

/// <summary>Título futuro que pode ser reduzido (CA 3): sem baixa, do último para o primeiro.</summary>
public sealed record ReducibleReceivable(Guid Id, string Number, string? Description, decimal Amount, DateOnly? DueDate);

/// <summary>O que a página do aditivo precisa para começar (CA 1 a 3, propositivo).</summary>
public sealed record AmendmentContext(decimal CurrentValue, DateOnly? PromisedDate, IReadOnlyList<ReducibleReceivable> Reducible, Dictionary<CostType, decimal>? Budget);

public sealed record AmendmentCommentInput(string? Comment, uint Version);

public sealed record AmendmentVersionInput(uint Version);
