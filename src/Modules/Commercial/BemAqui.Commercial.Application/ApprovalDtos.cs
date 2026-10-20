namespace BemAqui.Commercial.Application;

/// <summary>Pré-condição da aprovação (COM-04a CA 1): o que falta e em qual aba corrigir.</summary>
public sealed record ApprovalPrecondition(bool Ok, string Text, string? FixTab);

public sealed record ApprovalMilestone(string Description, decimal Percent, decimal Amount, string When);

public sealed record ApprovalAccount(Guid Id, string Label);

/// <summary>Item que merece atenção na aprovação (CA 3), só para quem vê custo: preço abaixo do custo ou digitado à mão.</summary>
public sealed record ApprovalAlert(int Sequence, string Description, bool BelowCost, bool PriceOverridden);

/// <summary>Diálogo "Aprovar e gerar contrato" (CA 1 a 3): pré-condições, o que vai ser criado, data e conta sugeridas.</summary>
public sealed record ApprovalPreview(
    IReadOnlyList<ApprovalPrecondition> Preconditions, decimal TotalValue, int Items, int? DeliveryDays, IReadOnlyList<ApprovalMilestone> Milestones,
    IReadOnlyList<ApprovalAccount> RevenueAccounts, Guid? SuggestedRevenueAccountId, string ProjectName);

public sealed record ApprovalCostView(decimal? MarginPct, IReadOnlyList<ApprovalAlert> Alerts);

public sealed record ApproveInput(DateOnly SignedAt, Guid RevenueAccountId, uint Version);

public sealed record ApprovalResult(Guid ContractId, string ContractNumber, Guid ProjectId, string ProjectNumber);
