using System.Text.Json.Serialization;
using BemAqui.Fiscal.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>Situação local da NF-e de saída (<c>04-fluxos</c> §1: <c>Draft → Processing → Authorized | Rejected | Denied</c>; <c>Authorized → Cancelled</c>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OutgoingInvoiceStatus>))]
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
/// NF-e de saída (<c>fiscal.outgoing_invoices</c>). Nasceu enxuta na FIS-01, só com o que a integração usa (ids, totais, situação, a
/// tentativa e o <c>payload_hash</c>, CA 5 e 16). A FIS-03a acrescenta o vínculo com o romaneio, o projeto e o cliente (só ids, ADR 0006), o
/// destinatário copiado do cadastro (<see cref="PersonalDataAttribute"/>: mascarado no diff, nunca no outbox nem no log), o transporte, os
/// totais calculados pelo <c>TaxCalculator</c>, o DANFE e o descarte do rascunho. A nota nunca é apagada: o rascunho é descartado (sem
/// número) e a nota com número só muda por cancelamento ou inutilização (FIS-03b).
/// </summary>
[DocumentName("A nota fiscal")]
public sealed class OutgoingInvoice : Entity, IAuditable, IConcurrencyVersioned
{
    public const int ReasonMaxLength = 500;
    public const int EmailMaxLength = 200;

    public InvoiceType Type { get; init; } = InvoiceType.NFe;

    /// <summary>Operação da nota (a do primeiro item): venda de produção própria ou revenda (nunca remessa, D4).</summary>
    public TaxOperation Operation { get; set; } = TaxOperation.OwnProductionSale;

    /// <summary>Natureza da operação (<c>natOp</c>) da regra do primeiro item.</summary>
    public string? NatureOfOperation { get; set; }

    /// <summary>Ambiente da configuração fiscal: o do rascunho e, na emissão, o que vale para a nota.</summary>
    public FiscalEnvironment Environment { get; set; } = FiscalEnvironment.Homologation;

    /// <summary>Romaneio expedido de onde a nota nasceu (CA 1): a nota só o referencia, nunca baixa o estoque (D4).</summary>
    public Guid? ShipmentId { get; init; }

    /// <summary>Número do romaneio copiado (lista e fila sem ir à Produção por linha).</summary>
    public string? ShipmentNumber { get; init; }

    public Guid? ProjectId { get; init; }

    public Guid? ContractId { get; init; }

    /// <summary>Cliente do projeto (o destinatário).</summary>
    public Guid? PartyId { get; init; }

    // Destinatário copiado do cadastro na montagem (CA 1): dado pessoal, mascarado na auditoria (CA 21).
    [PersonalData]
    public string? RecipientName { get; set; }

    [PersonalData]
    public string? RecipientDocument { get; set; }

    public RecipientType RecipientType { get; set; } = RecipientType.Contributor;

    public bool FinalConsumer { get; set; }

    [PersonalData]
    public string? RecipientStateRegistration { get; set; }

    [PersonalData]
    public string? RecipientEmail { get; set; }

    [PersonalData]
    public string? RecipientPhone { get; set; }

    [PersonalData]
    public string? RecipientStreet { get; set; }

    [PersonalData]
    public string? RecipientNumber { get; set; }

    [PersonalData]
    public string? RecipientComplement { get; set; }

    [PersonalData]
    public string? RecipientDistrict { get; set; }

    [PersonalData]
    public string? RecipientPostalCode { get; set; }

    public string? RecipientCityName { get; set; }

    /// <summary>Código IBGE do município do destinatário (7 dígitos): pré-condição da emissão (CA 6).</summary>
    public string? RecipientCityCode { get; set; }

    public string? RecipientState { get; set; }

    // Transporte (do romaneio e da modalidade padrão da FIS-02a).
    public int FreightMode { get; set; }

    public Guid? CarrierPartyId { get; set; }

    public int? Volumes { get; set; }

    public decimal? GrossWeight { get; set; }

    public string? VolumeSpecies { get; set; }

    /// <summary>Frete e outras despesas da nota, rateados nos itens pelo valor.</summary>
    public decimal Freight { get; set; }

    public decimal OtherExpenses { get; set; }

    /// <summary>Informações complementares (<c>infCpl</c>, até 5000): o endereço da obra vem preenchido (D4).</summary>
    public string? AdditionalInformation { get; set; }

    /// <summary>Totais calculados (jsonb): produtos, desconto, frete, tributos por código, total da nota e o custo do projeto.</summary>
    public InvoiceTotals Totals { get; set; } = InvoiceTotals.Empty;

    /// <summary>Soma dos tributos da nota (sem DIFAL): a coluna "Impostos" e o total do filtro da lista (CA 17).</summary>
    public decimal TaxAmount { get; private set; }

    /// <summary>Tributos marcados como custo na regra (CA 11): o imposto realizado do projeto pela nota.</summary>
    public decimal CostTaxAmount { get; set; }

    /// <summary>Por que o cálculo não fechou (ex.: alíquota do IPI da TIPI): a emissão espera a correção.</summary>
    public string? CalculationError { get; set; }

    /// <summary>"Enviar ao cliente" (padrão sim, CA 14): XML e DANFE ao e-mail de cobrança depois da autorização.</summary>
    public bool SendToCustomer { get; set; } = true;

    [PersonalData]
    public string? EmailCc { get; set; }

    public DateTimeOffset? EmailedAt { get; set; }

    /// <summary>Data da nota (dia de São Paulo da autorização): a receita e o imposto do projeto contam por ela.</summary>
    public DateOnly? IssuedOn { get; private set; }

    /// <summary>DANFE em PDF do Emissor, anexo de sistema da nota (lacuna L-c).</summary>
    public Guid? DanfeAttachmentId { get; private set; }

    /// <summary>Quando o <c>InvoiceAuthorized</c> foi para o outbox (uma vez só, mesmo com webhook e conferência ao mesmo tempo).</summary>
    public DateTimeOffset? AuthorizationPublishedAt { get; private set; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    /// <summary>Quem mandou emitir a última vez: recebe o aviso da rejeição (CA 8).</summary>
    public Guid? IssuedBy { get; set; }

    /// <summary>Rascunho descartado (exclusão lógica, sem número, CA 20): sai da fila e do índice de nota ativa.</summary>
    public DateTimeOffset? DiscardedAt { get; private set; }

    public Guid? DiscardedBy { get; private set; }

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

    /// <summary>Última reconsulta (fora da auditoria: a trilha é a mudança de situação).</summary>
    [NotAudited]
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

    /// <summary>Rascunho que ainda pode ser editado e emitido.</summary>
    public bool IsEditableDraft => Status == OutgoingInvoiceStatus.Draft && DiscardedAt is null;

    /// <summary>
    /// Nota ativa do romaneio (CA 1 e 19): rascunho, processando, contingência ou autorizada, sem descarte. O índice único parcial
    /// <c>ux_outgoing_invoices_active_shipment</c> usa a mesma regra.
    /// </summary>
    public bool IsActive => DiscardedAt is null && Status is not (OutgoingInvoiceStatus.Cancelled or OutgoingInvoiceStatus.Rejected or OutgoingInvoiceStatus.Denied);

    /// <summary>Situações fora da nota ativa (o filtro do índice único parcial).</summary>
    public static readonly OutgoingInvoiceStatus[] InactiveStatuses = [OutgoingInvoiceStatus.Cancelled, OutgoingInvoiceStatus.Rejected, OutgoingInvoiceStatus.Denied];

    public static OutgoingInvoice Create(decimal totalAmount, DateTimeOffset at) => new() { TotalAmount = Math.Round(totalAmount, 2, MidpointRounding.AwayFromZero), CreatedAt = at, UpdatedAt = at };

    /// <summary>Rascunho a partir do romaneio expedido (CA 1): os valores chegam depois, pelo montador e pelo cálculo.</summary>
    public static OutgoingInvoice NewDraft(Guid shipmentId, string shipmentNumber, Guid projectId, Guid contractId, Guid partyId, Guid? by, DateTimeOffset at) => new()
    {
        ShipmentId = shipmentId, ShipmentNumber = shipmentNumber, ProjectId = projectId, ContractId = contractId, PartyId = partyId, CreatedBy = by, CreatedAt = at, UpdatedAt = at,
        UpdatedBy = by,
    };

    /// <summary>Totais do cálculo (CA 3): o total da nota e o custo do projeto vêm da soma dos itens, nunca digitados.</summary>
    public void ApplyTotals(InvoiceTotals totals, string? calculationError)
    {
        ArgumentNullException.ThrowIfNull(totals);
        Totals = totals;
        TotalAmount = totals.InvoiceTotal;
        CostTaxAmount = totals.CostTax;
        TaxAmount = totals.TaxAmount;
        CalculationError = calculationError;
    }

    /// <summary>"Descartar rascunho" (CA 20): só o rascunho, que ainda não tem número; a linha fica, marcada.</summary>
    public Error? Discard(Guid? by, DateTimeOffset at)
    {
        if (!IsEditableDraft || Number is not null)
        {
            return Error.Conflict(FiscalErrorCodes.InvoiceClosed, "Só o rascunho sem número é descartado.");
        }

        DiscardedAt = at;
        DiscardedBy = by;
        return null;
    }

    /// <summary>
    /// "Corrigir e emitir de novo" (CA 8): a rejeitada volta a <c>Draft</c> com os dados e a próxima tentativa (a chave muda); o número
    /// gasto pela rejeição já foi para "Números a inutilizar". Denegada nunca volta (CA 9).
    /// </summary>
    public Error? Reopen(Guid? by, DateTimeOffset at)
    {
        if (Status == OutgoingInvoiceStatus.Denied)
        {
            return Error.Conflict(FiscalErrorCodes.InvoiceDenied, DeniedGuidance);
        }

        if (Status != OutgoingInvoiceStatus.Rejected)
        {
            return Error.Conflict(FiscalErrorCodes.InvoiceClosed, "Só a nota rejeitada volta para o rascunho.");
        }

        Attempt++;
        EmitterDocumentId = null;
        AccessKey = null;
        Series = null;
        Number = null;
        Protocol = null;
        PayloadHash = null;
        ProcessingSince = null;
        ContingencySince = null;
        Status = OutgoingInvoiceStatus.Draft;
        UpdatedAt = at;
        UpdatedBy = by;
        return null;
    }

    /// <summary>Orientação da denegada (CA 9).</summary>
    public const string DeniedGuidance = "A SEFAZ denegou a nota por irregularidade do destinatário. Fale com a contadora.";

    /// <summary>
    /// Autorizada: grava a data da nota (dia de São Paulo da autorização) e devolve true na primeira vez, para o <c>InvoiceAuthorized</c>
    /// ir uma vez só ao outbox (CA 7).
    /// </summary>
    public bool MarkAuthorizationPublished(DateOnly issuedOn, DateTimeOffset at)
    {
        if (Status != OutgoingInvoiceStatus.Authorized || AuthorizationPublishedAt is not null)
        {
            return false;
        }

        IssuedOn = issuedOn;
        AuthorizationPublishedAt = at;
        return true;
    }

    /// <summary>DANFE em PDF guardado (CA 7): uma vez só.</summary>
    public void AttachDanfe(Guid attachmentId) => DanfeAttachmentId ??= attachmentId;

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
    public const string InvoiceDenied = "fiscal_invoice_denied";
    public const string ShipmentNotShipped = "fiscal_shipment_not_shipped";
    public const string InvoiceExists = "fiscal_invoice_exists";
    public const string IssueBlocked = "fiscal_issue_blocked";
    public const string TotalsChanged = "fiscal_totals_changed";
    public const string TextTooLong = "fiscal_text_too_long";
    public const string InvoiceNotFound = "fiscal_invoice_not_found";
    public const string NotAuthorized = "fiscal_invoice_not_authorized";
}
