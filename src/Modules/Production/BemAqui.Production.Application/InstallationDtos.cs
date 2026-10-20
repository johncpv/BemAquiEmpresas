using BemAqui.Production.Domain;

namespace BemAqui.Production.Application;

public sealed record InstallationCreateInput(Guid ProjectId, IReadOnlyList<Guid>? ProjectItemIds, Guid InstallerId, decimal ContractValue, DateOnly PlannedStart, DateOnly PlannedEnd);

/// <summary>Sugestões do diálogo "Nova OS de instalação" (COM-06 CA 2, propositivo): itens livres, valor orçado e instalador.</summary>
public sealed record InstallationDefaults(IReadOnlyList<InstallationItemOption> Items, decimal? SuggestedValue, Guid? SuggestedInstallerId, string? SuggestedInstallerName);

public sealed record InstallationItemOption(Guid Id, int Sequence, string Description, decimal Quantity, string? CoveredBy);

public sealed record InstallationSummary(
    Guid Id, string Number, Guid ProjectId, string ProjectNumber, string ProjectName, Guid InstallerId, string InstallerName, DateOnly PlannedStart, DateOnly PlannedEnd,
    ProductionOrderStatus Status, int StepsDone, int StepsTotal);

public sealed record InstallationPage(IReadOnlyList<InstallationSummary> Items, int Page, int PageSize, int Total);

public sealed record InstallationStepView(Guid Id, int Sequence, string Name, DateTimeOffset? CompletedAt, string? CompletedByName);

public sealed record InstallationChecklistView(Guid Id, int Sequence, string Text, ChecklistResult? Result, DateTimeOffset? MarkedAt, string? MarkedByName);

public sealed record InstallationDetail(
    Guid Id, string Number, Guid ProjectId, string ProjectNumber, string ProjectName, Guid ContractId, Guid InstallerId, string InstallerName, decimal ContractValue,
    DateOnly PlannedStart, DateOnly PlannedEnd, ProductionOrderStatus Status, DateOnly? StartedOn, DateOnly? AcceptedOn, string? AcceptedByName, string? CancelReason,
    IReadOnlyList<InstallationStepView> Steps, IReadOnlyList<InstallationChecklistView> Checklist, IReadOnlyList<InstallationItemOption> Items, uint Version,
    BemAqui.Finance.Contracts.PayableRef? Payable = null);

public sealed record InstallationUpdateInput(decimal ContractValue, DateOnly PlannedStart, DateOnly PlannedEnd, uint Version);

public sealed record InstallationDateInput(DateOnly Date, uint Version);

public sealed record InstallationChecklistInput(ChecklistResult? Result, uint Version);

public sealed record InstallationAcceptInput(DateOnly Date, string? AcceptedByName, uint Version);

public sealed record InstallationCancelInput(string? Reason, uint Version);

public sealed record InstallationTemplateView(IReadOnlyList<string> Steps, IReadOnlyList<string> Checklist);

public sealed record InstallationTemplateInput(IReadOnlyList<string> Steps, IReadOnlyList<string> Checklist);

/// <summary>Resultado do aceite (COM-06 CA 9): o título do instalador confirmado, para o próximo passo.</summary>
public sealed record InstallationAcceptResult(Guid PayableId, string PayableNumber, DateOnly? PayableDueDate);
public sealed record InstallationVersionInput(uint Version);

public sealed record InstallerOption(Guid Id, string Name);
