namespace BemAqui.Finance.Application;

public sealed record ApprovalRuleView(
    Guid Id, int Level, decimal MinAmount, decimal? MaxAmount, Guid? AccountId, string? AccountLabel, Guid? ApproverGroupId, Guid? ApproverUserId, string ApproverName, bool IsDefault);

public sealed record ApprovalRuleInput(decimal MinAmount, decimal? MaxAmount, Guid? AccountId, Guid? ApproverGroupId, Guid? ApproverUserId, int Level);

public sealed record ApprovalLevelView(int Level, Guid RuleId, string ApproverName, decimal ReferenceAmount, string? SplitWarning, string Status);

/// <summary>Simulador (FIN-03b telas): quem aprovaria uma OP, com o valor de referência.</summary>
public sealed record ApprovalSimulation(decimal ReferenceAmount, string? SplitWarning, IReadOnlyList<ApprovalLevelView> Levels);

public sealed record ApproverOption(Guid Id, string Name, bool IsGroup);
