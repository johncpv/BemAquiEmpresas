using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application;

/// <summary>Conta com integração Inter ativa e testada, para o diálogo de emissão (CA 4).</summary>
public sealed record BillingAccountOption(Guid Id, string Name, bool Sandbox, bool IsDefault);

/// <summary>
/// Pré-condição do boleto (CA 10): o que falta aparece antes de confirmar, com o link para corrigir. <c>Blocking</c> falso é só aviso
/// (sem e-mail de cobrança, o boleto sai sem envio).
/// </summary>
public sealed record BillingPrecondition(string Code, string Text, string? FixLabel, string? FixLink, bool Blocking);

/// <summary>Diálogo "Emitir boleto" (CA 4, 5, 10, 11 e 15): tudo pré-preenchido pelos padrões.</summary>
public sealed record BillingSlipDraft(
    Guid ReceivableId,
    string ReceivableNumber,
    Guid PartyId,
    string PartyName,
    decimal Amount,
    DateOnly? ReceivableDueDate,
    bool IsOverdue,
    DateOnly SuggestedDueDate,
    decimal FinePct,
    decimal InterestMonthPct,
    decimal DiscountAmount,
    DateOnly? DiscountUntil,
    bool AutoEmail,
    string? Email,
    OverdueFineFrom OverdueFineFrom,
    string NonBusinessDayRule,
    decimal DiscountLimitPct,
    decimal DiscountLimitAmount,
    bool CanApprove,
    IReadOnlyList<BillingAccountOption> Accounts,
    IReadOnlyList<BillingPrecondition> Preconditions,
    Guid? CurrentSlipId,
    uint Version);

/// <summary>"Emitir boleto" (CA 4 a 6, 15): <c>Version</c> é a do título aberto na tela.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BillingSlipInput(Guid BankAccountId, DateOnly DueDate, decimal FinePct, decimal InterestMonthPct, decimal DiscountAmount, DateOnly? DiscountUntil, string? Message, bool SendEmail, uint Version);

/// <summary>"Enviar ao cliente" (CA 13): sem destinatário, o contato de cobrança; até 3 cópias.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BillingSlipSendInput(string? To, IReadOnlyList<string>? Cc);

/// <summary>"Cancelar boleto" (CA 16): motivo da lista mapeada do Inter.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BillingSlipCancelInput(BillingSlipCancelReason Reason);

/// <summary>Envio do boleto por e-mail (histórico, CA 13); o endereço vai mascarado.</summary>
public sealed record BillingSlipEmailView(Guid Id, string To, EmailStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, string? Error);

/// <summary>Aba "Boleto" do título (CA 12): sem o <c>provider_payload</c> e sem dado do pagador (CA 14).</summary>
public sealed record BillingSlipView(
    Guid Id,
    Guid ReceivableId,
    Guid BankAccountId,
    string BankAccountName,
    bool Sandbox,
    BillingSlipStatus Status,
    string OurReference,
    string? OurNumber,
    decimal Amount,
    DateOnly DueDate,
    DateOnly ReceivableDueDate,
    DateOnly FineFrom,
    decimal FinePct,
    decimal InterestMonthPct,
    decimal DiscountAmount,
    DateOnly? DiscountUntil,
    string? Message,
    string? Barcode,
    string? DigitableLine,
    string? PixCopyPaste,
    string? QrCode,
    Guid? PdfAttachmentId,
    string? FailureReason,
    string? FailureAction,
    BillingSlipCancelReason? CancelReason,
    string? CancelReasonText,
    bool CancelPending,
    string? CancelFailureReason,
    DateTimeOffset? CancelledAt,
    DateTimeOffset? ExpiredAt,
    DateTimeOffset? PaidAt,
    DateTimeOffset RequestedAt,
    string? RequestedByName,
    DateTimeOffset? IssuedAt,
    string? ApprovedByName,
    bool SendOnIssue,
    DateTimeOffset? ScheduledEmailAt,
    DateTimeOffset? LastEmailAt,
    IReadOnlyList<BillingSlipEmailView> Emails,
    uint Version);

/// <summary>Resposta das ações: o boleto e a mensagem da região do topo (ADR 0021).</summary>
public sealed record BillingSlipResult(BillingSlipView Slip, string Message);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BillingBatchPreviewInput(IReadOnlyList<Guid> ReceivableIds);

public sealed record BillingBatchItem(Guid ReceivableId, string Number, string PartyName, decimal Amount, DateOnly DueDate, string? Email);

public sealed record BillingBatchRefusal(Guid ReceivableId, string Number, string PartyName, string Reason);

/// <summary>Impacto em lista do lote (CA 19): elegíveis, recusados com o motivo e o total.</summary>
public sealed record BillingBatchPreview(IReadOnlyList<BillingBatchItem> Eligible, IReadOnlyList<BillingBatchRefusal> Refused, decimal Total, Guid? BankAccountId, bool Sandbox);

/// <summary>Cada título do lote com o seu <c>Idempotency-Key</c> (CA 19).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BillingBatchEntry(Guid ReceivableId, Guid ClientRequestId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BillingBatchInput(IReadOnlyList<BillingBatchEntry> Items, bool SendEmail);

public sealed record BillingBatchResult(int Requested, IReadOnlyList<Guid> SlipIds, IReadOnlyList<BillingBatchRefusal> Refused, string Message);
