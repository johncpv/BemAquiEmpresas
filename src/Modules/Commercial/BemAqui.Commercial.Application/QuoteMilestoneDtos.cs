using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Application;

public sealed record QuoteMilestoneView(Guid Id, int Sequence, string Description, decimal Percent, decimal Amount, MilestoneTrigger Trigger, TriggerEvent? TriggerEvent, DateOnly? DueDate, int? DaysAfterEvent);

/// <summary>Condições do orçamento (CA 1 a 4): marcos com % e valor, a soma e o aviso quando não fecha 100%.</summary>
public sealed record QuoteConditions(IReadOnlyList<QuoteMilestoneView> Milestones, decimal PercentSum, string? Warning, DateOnly? ValidUntil, Guid? DefaultPaymentTermId, uint Version);

public sealed record QuoteMilestoneInput(string Description, decimal Percent, MilestoneTrigger Trigger, TriggerEvent? TriggerEvent, DateOnly? DueDate, int? DaysAfterEvent);

public sealed record QuoteConditionsInput(IReadOnlyList<QuoteMilestoneInput> Milestones, DateOnly? ValidUntil, uint Version);

public sealed record QuoteLoseInput(QuoteLostReason? Reason, string? Detail, uint Version);

/// <summary>Cópia (CA 13): cliente, contato e obra do novo orçamento; nulos ficam os do original.</summary>
public sealed record QuoteCopyInput(Guid? PartyId, Guid? ContactId, Guid? SiteAddressId);

public sealed record QuoteCopyItemsInput(Guid SourceQuoteId, IReadOnlyList<Guid> ItemIds, uint Version);

public sealed record QuoteCopyOption(Guid Id, string Number, int Revision, string PartyName, QuoteStatus Status, decimal TotalPrice, IReadOnlyList<QuoteCopyItemOption> Items);

public sealed record QuoteCopyItemOption(Guid Id, string Description, decimal Quantity);
