using System.Text.Json.Serialization;
using BemAqui.Fiscal.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>De onde veio a NFS-e (FIS-06): emitida pelo Emissor Fiscal ou emitida no portal da prefeitura e registrada aqui (processo provisório, D5).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InvoiceOrigin>))]
public enum InvoiceOrigin
{
    Emitter,
    PortalRegistered,
}

/// <summary>
/// Serviço da NFS-e (jsonb <c>service</c>, FIS-06 CA 2): item da LC 116, código do serviço na prefeitura (<c>CodSrv</c>), CTN e NBS de
/// Configurações → Fiscal, o município da prestação (o da obra no item 7.02), a discriminação, o ISS e as retenções pela regra de prestação
/// de serviço, o CST e o <c>cClassTrib</c> do IBS/CBS e o valor líquido (serviço − ISS retido).
/// </summary>
public sealed record NfseServiceInfo(
    string ServiceItem, string? MunicipalCode, string? Ctn, string? Nbs, string? ServiceCityCode, string? ServiceCityName, IssLocation IssLocation, decimal IssRate,
    decimal IssAmount, bool IssWithheld, bool FederalWithholding, string Description, string? IbsCbsCst, string? IbsCbsClassification, decimal NetAmount);

/// <summary>Cancelamento feito no portal da prefeitura e registrado aqui (FIS-06 CA 9): motivo, protocolo, quem, quando e a data (São Paulo).</summary>
public sealed record PortalCancellation(string Reason, string Protocol, Guid? By, DateTimeOffset At, DateOnly On);

/// <summary>
/// NFS-e da instalação (FIS-06): a mesma <c>fiscal.outgoing_invoices</c> com <c>type = NFSe</c>, a OS de instalação aceita (só o id e o número,
/// ADR 0006), a NF-e do equipamento vinculada, o serviço (jsonb), o código de verificação, a origem (Emissor ou portal da prefeitura), o ISS
/// digitado da nota da prefeitura e o cancelamento registrado. Nunca é apagada: o rascunho é descartado e a nota com número só é cancelada.
/// </summary>
public sealed partial class OutgoingInvoice
{
    public const int VerificationCodeMaxLength = 60;
    public const int CancellationReasonMaxLength = 500;
    public const int CancellationProtocolMaxLength = 60;

    /// <summary>OS de instalação aceita de onde a NFS-e nasceu (CA 1 e 2).</summary>
    public Guid? InstallationOrderId { get; init; }

    /// <summary>Número da OS copiado (lista e fila sem ir à Produção por linha).</summary>
    public string? InstallationOrderNumber { get; init; }

    /// <summary>NF-e do equipamento do mesmo projeto (<c>linked_invoice_id</c>, venda mista D4).</summary>
    public Guid? LinkedInvoiceId { get; set; }

    /// <summary>Código de verificação da NFS-e (da prefeitura): com o número, identifica a nota.</summary>
    public string? VerificationCode { get; private set; }

    public InvoiceOrigin Origin { get; private set; } = InvoiceOrigin.Emitter;

    /// <summary>ISS como saiu na nota da prefeitura (só no registro do portal, CA 6); comparado ao calculado pela regra.</summary>
    public decimal? IssTyped { get; private set; }

    /// <summary>Serviço da NFS-e (jsonb); nulo na NF-e.</summary>
    public NfseServiceInfo? Service { get; set; }

    /// <summary>Cancelamento feito na prefeitura e registrado aqui (CA 9); nulo enquanto não houver.</summary>
    public PortalCancellation? PortalCancellation { get; private set; }

    /// <summary>Rascunho da NFS-e a partir da OS de instalação aceita (CA 2): os valores chegam depois, pelo montador e pelo cálculo.</summary>
    public static OutgoingInvoice NewNfseDraft(Guid installationOrderId, string orderNumber, Guid projectId, Guid contractId, Guid partyId, Guid? by, DateTimeOffset at) => new()
    {
        Type = InvoiceType.NFSe, Operation = TaxOperation.ServiceProvision, InstallationOrderId = installationOrderId, InstallationOrderNumber = orderNumber, ProjectId = projectId,
        ContractId = contractId, PartyId = partyId, CreatedBy = by, CreatedAt = at, UpdatedAt = at, UpdatedBy = by,
    };

    /// <summary>
    /// Resposta ou reconsulta do Emissor para a NFS-e (CA 3 e 4): a mesma transição da NF-e (<see cref="Apply"/>, sem regressão) e o código de
    /// verificação. Devolve se mudou.
    /// </summary>
    public bool ApplyNfse(EmitterNfeState remote, string? verificationCode, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(remote);
        var changed = Apply(remote, now);
        Series = null; // a NFS-e não tem série da SEFAZ: o número é o da prefeitura (e nunca vai para "Números a inutilizar")
        if (EmitterDocumentId == remote.DocumentId && NfseRules.CleanCode(verificationCode) is { } code && VerificationCode != code)
        {
            VerificationCode = code;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// "Registrar NFS-e emitida na prefeitura" (CA 6, D5): só o rascunho da NFS-e; a nota fica <c>Authorized</c> com a origem "Registrada
    /// (portal da prefeitura)", o número, o código de verificação, a data, o valor e o ISS como saíram na nota da prefeitura. O valor e o ISS
    /// digitados valem para a receita e para o imposto do projeto.
    /// </summary>
    public Error? RegisterFromPortal(long number, string verificationCode, decimal servicesAmount, decimal issTyped, Guid? by, DateTimeOffset at)
    {
        if (Type != InvoiceType.NFSe || !IsEditableDraft)
        {
            return Error.Conflict(FiscalErrorCodes.InvoiceClosed, "Só o rascunho da NFS-e é registrado.");
        }

        ArgumentException.ThrowIfNullOrEmpty(verificationCode);
        Number = number;
        VerificationCode = verificationCode;
        Origin = InvoiceOrigin.PortalRegistered;
        IssTyped = TaxCalculator.Round(issTyped);
        TotalAmount = TaxCalculator.Round(servicesAmount);
        Status = OutgoingInvoiceStatus.Authorized;
        AuthorizedAt = at;
        IssuedBy = by;
        UpdatedAt = at;
        UpdatedBy = by;
        return null;
    }

    /// <summary>
    /// "Registrar cancelamento feito na prefeitura" (CA 9): só a NFS-e autorizada; a nota fica <c>Cancelled</c> com a origem manual (motivo,
    /// protocolo, quem e quando), sem apagar nada. Devolve o erro, ou nulo.
    /// </summary>
    public Error? RegisterPortalCancellation(string reason, string protocol, Guid? by, DateTimeOffset at, DateOnly on)
    {
        if (Type != InvoiceType.NFSe)
        {
            return Error.Conflict(FiscalErrorCodes.InvoiceClosed, "O registro de cancelamento da prefeitura é só para a NFS-e.");
        }

        if (Status != OutgoingInvoiceStatus.Authorized)
        {
            return Error.Conflict(FiscalErrorCodes.NotAuthorized, Status == OutgoingInvoiceStatus.Cancelled ? "A NFS-e já está cancelada." : "Só a NFS-e autorizada é cancelada.");
        }

        ArgumentException.ThrowIfNullOrEmpty(reason);
        ArgumentException.ThrowIfNullOrEmpty(protocol);
        PortalCancellation = new PortalCancellation(reason, protocol, by, at, on);
        Status = OutgoingInvoiceStatus.Cancelled;
        UpdatedAt = at;
        UpdatedBy = by;
        return null;
    }
}
