using System.Text.Json.Serialization;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;

namespace BemAqui.Fiscal.Application.Invoices.Nfse;

/// <summary>Como a NFS-e sai agora (FIS-06 CA 3 e 5): pelo Emissor Fiscal (NFS-e do município habilitada) ou pelo portal da prefeitura (D5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NfseMode>))]
public enum NfseMode
{
    Emitter,
    Portal,
}

/// <summary>
/// "Emitir NFS-e" na fila, no aceite técnico ou no projeto (CA 2): só a OS de instalação aceita (e o projeto dela, quando a tela sabe: o
/// aceite acabou de acontecer e a fila ainda não recebeu o evento); o resto vem preenchido.
/// </summary>
public sealed record CreateNfseRequest(Guid InstallationOrderId, Guid? ProjectId = null);

/// <summary>"Salvar rascunho" da NFS-e: a discriminação, o envio ao cliente e a cópia. Nunca imposto (CA 2: nada digitado).</summary>
public sealed record UpdateNfseRequest(string? Description, bool SendToCustomer, string? EmailCc, uint Version);

/// <summary>"Registrar cancelamento feito na prefeitura" (CA 9): motivo, protocolo e o código do autenticador (2FA de novo, D6).</summary>
public sealed record RegisterNfseCancellationRequest(string? Reason, string? Protocol, string? Code, uint Version);

/// <summary>Um valor do rascunho para copiar no portal da prefeitura (CA 5), com o botão "Copiar" por campo.</summary>
public sealed record NfseCopyField(string Key, string Label, string Value);

public sealed record NfseItemView(Guid Id, int Sequence, Guid? ProjectItemId, string Description, decimal Quantity, decimal UnitPrice, decimal Discount, decimal GrossAmount, string? TaxRuleName,
    int? TaxRuleVersion, IReadOnlyList<TaxLine> Taxes, decimal CostTax);

/// <summary>Cancelamento registrado (CA 9): motivo, protocolo, quem e quando.</summary>
public sealed record NfseCancellationView(string Reason, string Protocol, string? ByName, DateTimeOffset At, DateOnly On);

/// <summary>O que a tela da NFS-e pode fazer agora (o servidor confere de novo).</summary>
public sealed record NfseActions(bool CanEdit, bool CanIssue, bool CanRegister, bool CanReopen, bool CanDiscard, bool CanRegisterCancellation, bool CanDownload, bool CanResend);

public sealed record NfseView(
    Guid Id, OutgoingInvoiceStatus Status, InvoiceOrigin Origin, FiscalEnvironment Environment, NfseMode Mode, string? PortalNotice, string PortalUrl, long? Number,
    string? VerificationCode, DateTimeOffset? AuthorizedAt, DateOnly? IssuedOn, int Attempt, Guid? ProjectId, string? ProjectNumber, string? ProjectName, Guid? InstallationOrderId,
    string? InstallationOrderNumber, Guid? PartyId, OutgoingInvoiceRecipientView Recipient, NfseServiceInfo? Service, IReadOnlyList<NfseItemView> Items, InvoiceTotals Totals,
    decimal TotalAmount, decimal CostTaxAmount, decimal IssCalculated, decimal? IssTyped, string? IssWarning, string? CalculationError, int? RejectionCode, string? RejectionReason,
    string? SuggestedAction, DateTimeOffset? ProcessingSince, Guid? XmlAttachmentId, Guid? PdfAttachmentId, bool SendToCustomer, string? EmailCc, DateTimeOffset? EmailedAt,
    Guid? LinkedInvoiceId, string? LinkedInvoiceLabel, NfseCancellationView? Cancellation, IReadOnlyList<NfseCopyField> PortalCopy, NfseActions Actions, DateTimeOffset CreatedAt,
    string? CreatedByName, string? IssuedByName, DateTimeOffset? DiscardedAt, uint Version);

/// <summary>
/// O que o <c>ConfirmDialog</c> mostra (CA 3 e 6): o modo, o ambiente, cada pré-condição com o link, os avisos e o impacto (tomador, serviço,
/// ISS, o que entra no custo do projeto).
/// </summary>
public sealed record NfseIssueCheckView(
    Guid InvoiceId, NfseMode Mode, FiscalEnvironment Environment, IReadOnlyList<IssuePrecondition> Preconditions, IReadOnlyList<string> Warnings, string? TakerName,
    NfseServiceInfo? Service, decimal TotalAmount, decimal IssAmount, decimal CostTaxAmount, string? ProjectNumber, bool ProjectCostApplies, uint Version);

/// <summary>Resultado de "Emitir NFS-e" (CA 3): a situação, a chave da tentativa e a mensagem da tela.</summary>
public sealed record NfseIssueResultView(Guid InvoiceId, OutgoingInvoiceStatus Status, string IdempotencyKey, string Message, NfseView Invoice);

/// <summary>Resultado de "Registrar NFS-e emitida na prefeitura" (CA 6): a nota e os avisos (ISS ou valor diferentes do calculado).</summary>
public sealed record NfseRegisterResultView(NfseView Invoice, IReadOnlyList<string> Warnings);

/// <summary>Instalação aceita esperando a NFS-e na fila "A emitir" (CA 1).</summary>
public sealed record InstallationQueueRow(
    Guid InstallationOrderId, string OrderNumber, Guid ProjectId, string ProjectNumber, string ProjectName, Guid PartyId, string PartyName, DateOnly AcceptedOn,
    int DaysSinceAcceptance, bool Late, decimal EstimatedAmount);
