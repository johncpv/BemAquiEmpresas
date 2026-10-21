using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application;

public sealed record PaymentOrderItemInput(Guid PayableId, decimal Amount, decimal ExpectedCharges = 0);

/// <summary>Nova OP (CA 2): títulos com o valor a pagar (até o saldo líquido) e a data prevista (padrão: o menor vencimento).</summary>
public sealed record PaymentOrderInput(IReadOnlyList<PaymentOrderItemInput> Items, DateOnly? ExpectedDate);

public sealed record PaymentOrderListQuery(string? Situation, bool AwaitingMe, bool SelfApproved);

public sealed record PaymentOrderSummary(
    Guid Id, string Number, PaymentOrderStatus Status, decimal Total, DateOnly ExpectedDate, Guid? RequestedBy, string? RequesterName, int Items, bool SelfApproved, DateTimeOffset SubmittedAt);

public sealed record PaymentOrderPage(IReadOnlyList<PaymentOrderSummary> Items, int Page, int PageSize, int Total);

/// <summary>Retrato do envio (CA 4): fornecedor, valores e para onde vai o dinheiro, congelados na OP.</summary>
public sealed record PayeeSnapshot(
    Guid PartyId, string PartyName, string? PartyDocument, DateTimeOffset? PartyCreatedAt, string PayableNumber, string Description, decimal NetAmount, decimal Retentions,
    decimal Amount, decimal ExpectedCharges, decimal ApprovedTotal, DateOnly? DueDate, PaymentDetailsInput Details, DateTimeOffset? PaymentMethodChangedAt);

/// <summary>Item visto na OP: o retrato mascarado e os avisos âmbar (CA 6).</summary>
public sealed record PaymentOrderItemView(
    Guid Id, Guid PayableId, string PayableNumber, string Description, string PartyName, decimal NetAmount, decimal Retentions, decimal Amount, decimal ExpectedCharges,
    decimal ApprovedTotal, DateOnly? DueDate, PaymentDetailsInput Payment, IReadOnlyList<string> Warnings, decimal PaidTotal, decimal? InstallationBudget = null);

public sealed record PaymentOrderDecisionView(PaymentOrderDecisionKind Kind, PaymentOrderDecision Decision, string? Comment, string? DecidedByName, DateTimeOffset DecidedAt, bool SelfApproved);

public sealed record PaymentOrderDetail(
    PaymentOrderSummary Summary, IReadOnlyList<PaymentOrderItemView> Items, IReadOnlyList<PaymentOrderDecisionView> Decisions, bool IsRequester, bool CanDecide,
    string? CancelReason, string? ReversalReason, DateOnly? PaidOn, Guid? BankAccountId, uint Version, IReadOnlyList<ApprovalLevelView>? Levels = null);

public sealed record DecisionInput(string? Comment, uint Version);

/// <summary>Acréscimos e desconto por item no pagamento (padrão: os acréscimos previstos como juros).</summary>
public sealed record PayItemInput(Guid ItemId, decimal Interest, decimal Fine, decimal Discount);

public sealed record PayInput(Guid BankAccountId, DateOnly Date, IReadOnlyList<PayItemInput> Items, string? BackdateReason, uint Version);

/// <summary>Reenvio com os novos acréscimos (CA 14): volta a <c>Pending</c> com novo retrato.</summary>
public sealed record ResubmitInput(IReadOnlyList<PayItemInput> Items, uint Version);

public sealed record PaymentResult(PaymentOrderDetail Order, decimal Total, IReadOnlyList<RetentionSuggestion> Retentions);

/// <summary>Título pago que tinha retenção: o próximo passo sugere o título do recolhimento (CA 19).</summary>
public sealed record RetentionSuggestion(Guid PayableId, string PayableNumber, decimal Amount, DateOnly SuggestedDueDate);
