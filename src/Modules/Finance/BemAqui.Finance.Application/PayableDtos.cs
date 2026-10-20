using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application;

/// <summary>Título a pagar manual (FIN-02 CA 1, 2, 4 e 6).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PayableInput(
    Guid PartyId, string Description, Guid AccountId, Guid? CostCenterId, decimal Amount, DateOnly IssueDate, DateOnly DueDate,
    decimal RetentionIss, decimal RetentionInss, decimal RetentionIr, decimal RetentionPcc, PaymentDetailsInput? PaymentDetails);

/// <summary>Parcelas pela condição (CA 3): as retenções ficam na primeira parcela.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PayableInstallmentsInput(
    Guid PartyId, string Description, Guid AccountId, Guid? CostCenterId, decimal Amount, DateOnly IssueDate, Guid PaymentTermId, DateOnly BaseDate,
    decimal RetentionIss, decimal RetentionInss, decimal RetentionIr, decimal RetentionPcc, PaymentDetailsInput? PaymentDetails);

/// <summary>Dados editáveis (CA 11 e 13): em OP, só descrição e vencimento.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PayableUpdateInput(
    string Description, Guid AccountId, Guid? CostCenterId, decimal Amount, DateOnly DueDate,
    decimal RetentionIss, decimal RetentionInss, decimal RetentionIr, decimal RetentionPcc, uint Version);

/// <summary>Forma de pagamento nova (CA 6 e 7); null tira a forma.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PaymentMethodInput(PaymentDetailsInput? PaymentDetails, uint Version);

public sealed record PayableListQuery(string? Q, string? Situation, Guid? PartyId, DateOnly? DueFrom, DateOnly? DueTo);

/// <summary>Título a pagar: a lista mostra só o tipo da forma de pagamento; os dados vêm mascarados no detalhe (CA 8).</summary>
public sealed record PayableDetail(
    Guid Id,
    string Number,
    Guid PartyId,
    string PartyName,
    string Description,
    Guid AccountId,
    string AccountName,
    Guid? CostCenterId,
    decimal Amount,
    decimal RetentionIss,
    decimal RetentionInss,
    decimal RetentionIr,
    decimal RetentionPcc,
    decimal NetAmount,
    decimal PaidAmount,
    decimal Balance,
    DateOnly IssueDate,
    DateOnly? DueDate,
    bool IsEstimate,
    PayableStatus Status,
    bool IsOverdue,
    bool IsDueToday,
    DocumentOrigin Origin,
    Guid? OriginId,
    string? OpeningReference,
    Guid? InstallmentGroupId,
    DateOnly? Competence,
    PaymentMethodKind? PaymentMethod,
    DateTimeOffset? PaymentMethodChangedAt,
    string? CancelReason,
    DateTimeOffset? CancelledAt,
    uint Version);

public sealed record PayableTotals(decimal Open, decimal Overdue);

public sealed record PayablePage(PagedList<PayableDetail> Page, PayableTotals Totals);

public sealed record PayablesCreated(IReadOnlyList<PayableDetail> Items, string? HolderWarning, string? BoletoWarning);

/// <summary>Fornecedor ou instalador para o seletor (CA 2), com a condição padrão; documento só de PJ.</summary>
public sealed record FinanceSupplierOption(Guid Id, string Name, string? Document, Guid? DefaultPaymentTermId);

/// <summary>Despesa recorrente (CA 16).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecurringExpenseInput(
    string Description, Guid PartyId, Guid AccountId, Guid? CostCenterId, decimal Amount, int DueDay, DateOnly Start, DateOnly? End, int? LeadDays, PaymentDetailsInput? PaymentDetails);

public sealed record RecurringExpenseDetail(
    Guid Id, string Description, Guid PartyId, string PartyName, Guid AccountId, Guid? CostCenterId, decimal Amount, int DueDay, DateOnly Start, DateOnly? End, int LeadDays, bool IsActive,
    PaymentMethodKind? PaymentMethod, IReadOnlyList<DateOnly> NextDueDates, bool HasPayables);
