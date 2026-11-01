using System.Text.Json.Serialization;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application;

/// <summary>Título a receber manual (FIN-01 CA 1 e 2).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceivableInput(Guid PartyId, string Description, Guid AccountId, Guid? CostCenterId, Guid? ProjectId, decimal Amount, DateOnly IssueDate, DateOnly DueDate);

/// <summary>Parcelamento pela condição de pagamento (CA 3): cada parcela vira um título.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceivableInstallmentsInput(Guid PartyId, string Description, Guid AccountId, Guid? CostCenterId, Guid? ProjectId, decimal Amount, DateOnly IssueDate, Guid PaymentTermId, DateOnly BaseDate);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SplitPreviewInput(Guid PaymentTermId, decimal Amount, DateOnly BaseDate);

/// <summary>Dados editáveis (CA 19): o valor só muda em título manual sem baixa.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReceivableUpdateInput(string Description, Guid AccountId, Guid? CostCenterId, Guid? ProjectId, decimal Amount, uint Version);

/// <summary>Vencimento novo com motivo (D14).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DueDateInput(DateOnly DueDate, string Reason, uint Version);

/// <summary>Baixa (CA 8 a 10, 13 a 15).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SettlementInput(Guid BankAccountId, DateOnly Date, decimal Principal, decimal Interest, decimal Fine, decimal Discount, string? BackdateReason, uint Version, bool CancelBillingSlip = false);

/// <summary>Quitação do saldo com desconto, sem transação (CA 11).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WriteOffInput(DateOnly Date, string? BackdateReason, uint Version, bool CancelBillingSlip = false);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReasonInput(string Reason, uint Version);

public sealed record ReceivableListQuery(string? Q, string? Situation, Guid? PartyId, DateOnly? DueFrom, DateOnly? DueTo, Guid? ContractId = null, bool WithBillingSlip = false);

/// <summary>Título para a lista e o detalhe; <c>IsOverdue</c> e <c>IsDueToday</c> são calculados (CA 6 e 7).</summary>
public sealed record ReceivableDetail(
    Guid Id,
    string Number,
    Guid PartyId,
    string PartyName,
    string Description,
    Guid AccountId,
    string AccountName,
    Guid? CostCenterId,
    Guid? ProjectId,
    decimal Amount,
    decimal PaidAmount,
    decimal Balance,
    decimal Interest,
    decimal Fine,
    decimal Discount,
    DateOnly IssueDate,
    DateOnly? DueDate,
    ReceivableStatus Status,
    bool IsOverdue,
    bool IsDueToday,
    DocumentOrigin Origin,
    Guid? OriginId,
    string? OpeningReference,
    Guid? InstallmentGroupId,
    string? CancelReason,
    DateTimeOffset? CancelledAt,
    uint Version,
    string? ProjectNumber = null,
    TriggerEvent? TriggerEvent = null,
    Guid? ContractId = null,
    Guid? BillingSlipId = null,
    BillingSlipStatus? BillingSlipStatus = null);

/// <summary>Totais do filtro inteiro (rodapé da lista).</summary>
public sealed record ReceivableTotals(decimal Open, decimal Overdue);

public sealed record ReceivablePage(BemAqui.BuildingBlocks.Paging.PagedList<ReceivableDetail> Page, ReceivableTotals Totals);

/// <summary>Os títulos gravados de uma vez (manual: um; parcelamento: um por parcela).</summary>
public sealed record ReceivablesCreated(IReadOnlyList<ReceivableDetail> Items);

public sealed record SettlementView(
    Guid Id,
    SettlementKind Kind,
    Guid? BankAccountId,
    string? BankAccountName,
    DateOnly Date,
    decimal Principal,
    decimal Interest,
    decimal Fine,
    decimal Discount,
    decimal Total,
    string? BackdateReason,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReversedAt,
    string? ReversalReason,
    SettlementSource Source = SettlementSource.Manual,
    Guid? WebhookEventId = null);

public sealed record DiscountLine(Guid ReceivableId, string Number, string PartyName, DateOnly Date, decimal Discount, decimal BalanceBefore, decimal Pct, Guid? UserId, DateTimeOffset At, bool AboveLimit);

/// <summary>Cancelamento do período; <c>IsPayable</c>: título a pagar (FIN-02), senão a receber.</summary>
public sealed record CancellationLine(Guid ReceivableId, string Number, string PartyName, decimal Balance, string Reason, Guid? UserId, DateTimeOffset At, bool IsPayable = false);

public sealed record DiscountsAndCancellations(DateOnly From, DateOnly To, IReadOnlyList<DiscountLine> Discounts, IReadOnlyList<CancellationLine> Cancellations, decimal TotalDiscount, decimal TotalCancelled);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FinanceLimitsInput(decimal DiscountApprovalPct, decimal DiscountApprovalAmount, decimal CancelApprovalAmount, int BackdateDays, bool OwnerCanApproveOwn = false, decimal PaymentTolerance = 0, decimal TransferNotifyAmount = 10_000,
    decimal? AdjustedDefaultThresholdPct = null, decimal? OfxBankEntryCap = null);

/// <summary>Cliente para o seletor do título (CA 2): só PJ mostra o documento (CPF não aparece em lista, D9).</summary>
public sealed record CustomerOption(Guid Id, string Name, string? Document, Guid? DefaultPaymentTermId);
