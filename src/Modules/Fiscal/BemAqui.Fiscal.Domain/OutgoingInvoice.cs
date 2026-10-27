using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>Situação local da NF-e de saída (<c>04-fluxos</c> §1: <c>Draft → Processing → Authorized | Rejected | Denied</c>; <c>Authorized → Cancelled</c>).</summary>
public enum OutgoingInvoiceStatus
{
    Draft,
    Processing,

    /// <summary>Sem resposta da SEFAZ (contingência no Emissor): nunca dispara reemissão (CA 6).</summary>
    ContingencyPending,
    Authorized,
    Rejected,
    Denied,
    Cancelled,
}

/// <summary>Como o envio terminou para a nota (CA 4 a 6).</summary>
public enum SubmissionOutcome
{
    /// <summary>O Emissor respondeu: a situação local já é a dele.</summary>
    Answered,

    /// <summary>Falha de rede, tempo limite ou 5xx: não se sabe se a nota saiu; fica <c>Processing</c> e a próxima tentativa usa a mesma chave.</summary>
    Uncertain,

    /// <summary>O Emissor recusou antes de criar o documento (400, 401, 403, 429): a nota volta para <c>Draft</c>, sem gastar número.</summary>
    NotCreated,
}

/// <summary>O que o Emissor disse da nota (resposta da emissão ou reconsulta), já nos tipos do BemAqui.</summary>
public sealed record EmitterNfeState(
    Guid DocumentId,
    FiscalDocumentStatus Status,
    string? AccessKey,
    int Series,
    long Number,
    string? Protocol,
    DateTimeOffset? AuthorizedAt,
    int? RejectionCode,
    string? RejectionReason,
    string? SuggestedAction);

/// <summary>
/// NF-e de saída (<c>fiscal.outgoing_invoices</c>). Nasce enxuta na FIS-01, só com o que a integração usa (ids, totais, situação, a
/// tentativa e o <c>payload_hash</c>, CA 5 e 16): nada do destinatário. A FIS-03a acrescenta o vínculo com o projeto, os itens e o
/// destinatário, que nunca vão para o outbox, a auditoria ou o log.
/// </summary>
[DocumentName("A nota fiscal")]
public sealed class OutgoingInvoice : Entity, IAuditable, IConcurrencyVersioned
{
    public const int ReasonMaxLength = 500;

    public OutgoingInvoiceStatus Status { get; private set; } = OutgoingInvoiceStatus.Draft;

    /// <summary>Tentativa de emissão: começa em 1 e só sobe depois de uma rejeição (CA 5).</summary>
    public int Attempt { get; private set; } = 1;

    /// <summary>SHA-256 (hex) do payload canônico enviado, gravado ao entrar em <c>Processing</c> (CA 5).</summary>
    public string? PayloadHash { get; private set; }

    public decimal TotalAmount { get; set; }

    public Guid? EmitterDocumentId { get; private set; }

    public string? AccessKey { get; private set; }

    public int? Series { get; private set; }

    public long? Number { get; private set; }

    public string? Protocol { get; private set; }

    public DateTimeOffset? AuthorizedAt { get; private set; }

    public int? RejectionCode { get; private set; }

    public string? RejectionReason { get; private set; }

    public string? SuggestedAction { get; private set; }

    /// <summary>Desde quando a nota espera desfecho (para a conferência de pendentes, CA 14).</summary>
    public DateTimeOffset? ProcessingSince { get; private set; }

    /// <summary>Desde quando está em contingência (alerta depois de 2 h, CA 12).</summary>
    public DateTimeOffset? ContingencySince { get; private set; }

    public DateTimeOffset? LastCheckedAt { get; private set; }

    /// <summary>Anexo de sistema com o XML autorizado (dono <c>OutgoingInvoice</c>, CA 13).</summary>
    public Guid? XmlAttachmentId { get; private set; }

    public string? XmlSha256 { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    /// <summary><c>nfe-{invoiceId}-{tentativa}</c> (CA 5, ADR 0027 §4).</summary>
    public string IdempotencyKey => "nfe-" + Id.ToString("D") + "-" + Attempt.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool IsPending => Status is OutgoingInvoiceStatus.Processing or OutgoingInvoiceStatus.ContingencyPending;

    public bool IsFinal => Status is OutgoingInvoiceStatus.Authorized or OutgoingInvoiceStatus.Denied or OutgoingInvoiceStatus.Cancelled;

    public static OutgoingInvoice Create(decimal totalAmount, DateTimeOffset at) => new() { TotalAmount = Math.Round(totalAmount, 2, MidpointRounding.AwayFromZero), CreatedAt = at };

    /// <summary>
    /// Antes de cada POST de emissão (CA 5 e 6). <c>Draft</c>: grava o hash e entra em <c>Processing</c>. <c>Rejected</c>: a correção é
    /// nova emissão, com a tentativa seguinte. <c>Processing</c>: reenvio com a mesma chave, só com o mesmo payload. <c>ContingencyPending</c>
    /// nunca reemite.
    /// </summary>
    public Error? BeginSubmission(string payloadHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(payloadHash);
        switch (Status)
        {
            case OutgoingInvoiceStatus.Draft:
                break;
            case OutgoingInvoiceStatus.Rejected:
                Attempt++;
                EmitterDocumentId = null;
                AccessKey = null;
                Series = null;
                Number = null;
                Protocol = null;
                RejectionCode = null;
                RejectionReason = null;
                SuggestedAction = null;
                break;
            case OutgoingInvoiceStatus.Processing:
                return string.Equals(PayloadHash, payloadHash, StringComparison.Ordinal)
                    ? null
                    : Error.Conflict(FiscalErrorCodes.PayloadChanged, "A nota mudou depois do envio. Aguarde a resposta do Emissor Fiscal antes de alterar.");
            case OutgoingInvoiceStatus.ContingencyPending:
                return Error.Conflict(FiscalErrorCodes.ContingencyPending, "A nota está em contingência na SEFAZ. Aguarde a confirmação; não emita de novo.");
            default:
                return Error.Conflict(FiscalErrorCodes.InvoiceClosed, "A nota já tem desfecho e não pode ser enviada de novo.");
        }

        PayloadHash = payloadHash;
        Status = OutgoingInvoiceStatus.Processing;
        ProcessingSince = now;
        ContingencySince = null;
        return null;
    }

    /// <summary>O Emissor recusou antes de criar o documento: volta para <c>Draft</c> com a mesma tentativa (nenhum número gasto).</summary>
    public void SubmissionNotCreated()
    {
        if (Status == OutgoingInvoiceStatus.Processing && EmitterDocumentId is null)
        {
            Status = OutgoingInvoiceStatus.Draft;
            PayloadHash = null;
            ProcessingSince = null;
        }
    }

    /// <summary>
    /// Aplica o que o Emissor disse (resposta da emissão, reconsulta do webhook ou consulta forçada, CA 11 e 14). Idempotente: o mesmo
    /// estado duas vezes não muda nada; nunca regride uma nota com desfecho. Devolve se mudou.
    /// </summary>
    public bool Apply(EmitterNfeState remote, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(remote);
        LastCheckedAt = now;
        if (Status == OutgoingInvoiceStatus.Draft || (EmitterDocumentId is { } known && known != remote.DocumentId))
        {
            return false; // documento de outra tentativa: não é desta nota agora
        }

        var target = remote.Status switch
        {
            FiscalDocumentStatus.Received or FiscalDocumentStatus.Processing => OutgoingInvoiceStatus.Processing,
            FiscalDocumentStatus.ContingencyPending => OutgoingInvoiceStatus.ContingencyPending,
            FiscalDocumentStatus.Authorized => OutgoingInvoiceStatus.Authorized,
            FiscalDocumentStatus.Rejected => OutgoingInvoiceStatus.Rejected,
            FiscalDocumentStatus.Denied => OutgoingInvoiceStatus.Denied,
            FiscalDocumentStatus.Cancelled => OutgoingInvoiceStatus.Cancelled,
            _ => Status,
        };
        if (!Allowed(Status, target))
        {
            return false;
        }

        var changed = target != Status || EmitterDocumentId is null || (remote.AccessKey is not null && AccessKey != remote.AccessKey);
        EmitterDocumentId = remote.DocumentId;
        AccessKey = remote.AccessKey ?? AccessKey;
        Series = remote.Series;
        Number = remote.Number > 0 ? remote.Number : Number;
        Protocol = remote.Protocol ?? Protocol;
        if (target == OutgoingInvoiceStatus.Authorized)
        {
            AuthorizedAt = remote.AuthorizedAt ?? AuthorizedAt ?? now;
        }

        if (target is OutgoingInvoiceStatus.Rejected or OutgoingInvoiceStatus.Denied)
        {
            RejectionCode = remote.RejectionCode;
            RejectionReason = Truncate(remote.RejectionReason);
            SuggestedAction = Truncate(remote.SuggestedAction);
        }

        if (target == OutgoingInvoiceStatus.ContingencyPending)
        {
            ContingencySince ??= now;
        }
        else
        {
            ContingencySince = null;
        }

        Status = target;
        return changed;
    }

    /// <summary>XML autorizado guardado (CA 13): uma vez só.</summary>
    public void AttachAuthorizedXml(Guid attachmentId, string sha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(sha256);
        if (XmlAttachmentId is null)
        {
            XmlAttachmentId = attachmentId;
            XmlSha256 = sha256;
        }
    }

    /// <summary>Transições aceitas a partir do Emissor (sem regressão de nota com desfecho).</summary>
    public static bool Allowed(OutgoingInvoiceStatus from, OutgoingInvoiceStatus to) => (from, to) switch
    {
        _ when from == to => true,
        (OutgoingInvoiceStatus.Processing or OutgoingInvoiceStatus.ContingencyPending, _) => to is not OutgoingInvoiceStatus.Draft,
        (OutgoingInvoiceStatus.Authorized, OutgoingInvoiceStatus.Cancelled) => true,
        _ => false,
    };

    private static string? Truncate(string? value) => value is { Length: > ReasonMaxLength } ? value[..ReasonMaxLength] : value;
}

/// <summary>Códigos de erro estáveis do módulo Fiscal (contrato com o front).</summary>
public static class FiscalErrorCodes
{
    public const string PayloadChanged = "fiscal_payload_changed";
    public const string ContingencyPending = "fiscal_contingency_pending";
    public const string InvoiceClosed = "fiscal_invoice_closed";
    public const string IntegrationMisconfigured = "fiscal_integration_misconfigured";
    public const string NotConfigured = "fiscal_not_configured";
    public const string Inactive = "fiscal_integration_inactive";
    public const string EnvironmentNotAllowed = "fiscal_environment_not_allowed";
    public const string Validation = "fiscal_validation";
    public const string Conflict = "fiscal_conflict";
    public const string NotFound = "fiscal_not_found";
    public const string RateLimited = "fiscal_rate_limited";
    public const string Unavailable = "fiscal_unavailable";
    public const string Rejected = "fiscal_rejected";
}
