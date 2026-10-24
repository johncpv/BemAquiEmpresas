namespace BemAqui.Finance.Application;

/// <param name="DocumentType">Tipo de documento da regra (<c>PaymentOrder</c> ou <c>PurchaseOrder</c>, CMP-03a): o seletor da tela de Alçadas.</param>
public sealed record ApprovalRuleView(
    Guid Id, int Level, decimal MinAmount, decimal? MaxAmount, Guid? AccountId, string? AccountLabel, Guid? ApproverGroupId, Guid? ApproverUserId, string ApproverName, bool IsDefault,
    string DocumentType);

/// <param name="DocumentType">Sem valor, ordem de pagamento (como na FIN-03b); a regra não muda de tipo depois de criada.</param>
public sealed record ApprovalRuleInput(decimal MinAmount, decimal? MaxAmount, Guid? AccountId, Guid? ApproverGroupId, Guid? ApproverUserId, int Level, string? DocumentType = null);

public sealed record ApprovalLevelView(int Level, Guid RuleId, string ApproverName, decimal ReferenceAmount, string? SplitWarning, string Status);

/// <summary>Simulador (FIN-03b telas): quem aprovaria uma OP (ou um pedido de compra), com o valor de referência.</summary>
public sealed record ApprovalSimulation(decimal ReferenceAmount, string? SplitWarning, IReadOnlyList<ApprovalLevelView> Levels);

public sealed record ApproverOption(Guid Id, string Name, bool IsGroup);
