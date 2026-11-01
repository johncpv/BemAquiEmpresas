using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application.Emitter;

/// <summary>
/// O único jeito do módulo Fiscal falar com o EmissorFiscal (FIS-01 CA 2, ADR 0027 §1, <c>02-arquitetura</c> §6.1). Os tipos são do
/// BemAqui (inglês, <c>decimal</c>, <c>DateTimeOffset</c>), nunca os DTOs do Emissor; o adaptador <c>EmissorFiscalHttpEmitter</c> traduz.
/// Toda falha volta como <see cref="Error"/> com os códigos de <see cref="FiscalErrorCodes"/>: <c>fiscal_unavailable</c> (rede, tempo
/// limite, 5xx) é "não sei se saiu" na emissão, e nunca vira chave nova (CA 6).
/// </summary>
public interface IFiscalEmitter
{
    /// <summary>Monta a nota sem transmitir e sem gastar número (<c>POST /api/v1/nfe/previa</c>). A DANFE em HTML do Emissor nunca é repassada.</summary>
    Task<Result<NfePreview>> PreviewAsync(NfeIssueRequest request, CancellationToken cancellationToken = default);

    /// <summary>Emite com o <c>Idempotency-Key</c> dado (<c>nfe-{invoiceId}-{tentativa}</c>): 201, 202 e 422 voltam como documento.</summary>
    Task<Result<NfeDocument>> IssueNfeAsync(NfeIssueRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Situação guardada no Emissor (<c>GET /api/v1/nfe/{chave}</c>): a reconsulta do webhook (CA 11).</summary>
    Task<Result<NfeDocument>> GetNfeAsync(string accessKey, CancellationToken cancellationToken = default);

    /// <summary>Consulta forçada na SEFAZ (<c>POST /api/v1/nfe/{chave}/consultar</c>, lacuna L-a): a conferência de pendentes (CA 14).</summary>
    Task<Result<NfeDocument>> ForceConsultAsync(string accessKey, CancellationToken cancellationToken = default);

    /// <summary>XML autorizado (<c>nfeProc</c>), para a guarda do BemAqui (CA 13).</summary>
    Task<Result<FiscalFile>> GetXmlAsync(string accessKey, CancellationToken cancellationToken = default);

    /// <summary>DANFE em PDF (lacuna L-c). Nunca HTML.</summary>
    Task<Result<FiscalFile>> GetDanfePdfAsync(string accessKey, CancellationToken cancellationToken = default);

    Task<Result<NfeEventResult>> CancelNfeAsync(string accessKey, string justification, CancellationToken cancellationToken = default);

    Task<Result<NfeEventResult>> CorrectNfeAsync(string accessKey, string correction, CancellationToken cancellationToken = default);

    Task<Result<NumberRangeInvalidationResult>> InvalidateRangeAsync(NumberRangeInvalidation request, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<NfeEventResult>>> GetEventsAsync(string accessKey, CancellationToken cancellationToken = default);

    Task<Result<SefazStatus>> SefazStatusAsync(string? issuerDocument, CancellationToken cancellationToken = default);

    /// <summary>Quem é a chave (<c>GET /api/v1/me</c>, lacuna L-e): tenant, escopos efetivos, emitentes, ambiente e certificado (CA 8).</summary>
    Task<Result<EmitterAccount>> GetAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>Repassa o A1 ao cofre do Emissor, só em memória (FIS-02a; ADR 0027 §3). O BemAqui nunca guarda o arquivo nem a senha.</summary>
    Task<Result<CertificateUploadResult>> UploadCertificateAsync(string issuerDocument, ReadOnlyMemory<byte> pfx, Secret password, CancellationToken cancellationToken = default);

    /// <summary>Emite a NFS-e (<c>POST /api/v1/nfse</c>) com o <c>Idempotency-Key</c> <c>nfse-{invoiceId}-{tentativa}</c> (FIS-06 CA 3).</summary>
    Task<Result<NfseDocument>> IssueNfseAsync(NfseIssueRequest request, string idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Situação guardada no Emissor (<c>GET /api/v1/nfse/{id}</c>).</summary>
    Task<Result<NfseDocument>> GetNfseAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Consulta na prefeitura agora (<c>POST /api/v1/nfse/{id}/consultar</c>): a conferência de pendentes enquanto não há webhook de NFS-e (FIS-06 CA 4).</summary>
    Task<Result<NfseDocument>> ForceConsultNfseAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>XML da NFS-e autorizada como a prefeitura devolveu (<c>GET /api/v1/nfse/{id}/xml</c>).</summary>
    Task<Result<FiscalFile>> GetNfseXmlAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>PDF da prefeitura (<c>GET /api/v1/nfse/{id}/pdf</c>); o redirecionamento para o link dela fica como falha (sem PDF guardado).</summary>
    Task<Result<FiscalFile>> GetNfsePdfAsync(Guid documentId, CancellationToken cancellationToken = default);
}

/// <summary>Endereço e chave do Emissor para um tenant. A chave só existe na memória do request ou do job.</summary>
public sealed record FiscalEmitterConnection(Uri BaseUrl, Secret ApiKey, Guid TenantId);

/// <summary>Cria o emissor de uma conexão (o adaptador HTTP; nos testes, o fake).</summary>
public interface IFiscalEmitterFactory
{
    IFiscalEmitter Create(FiscalEmitterConnection connection);
}

// ---------- Emissão ----------

/// <summary><c>tpNF</c> da SEFAZ, sem conversão (L-l, conferido pela suíte de contrato): 0 entrada, 1 saída.</summary>
public enum NfeOperationType
{
    Inbound = 0,
    Outbound = 1,
}

/// <summary><c>finNFe</c>.</summary>
public enum NfePurpose
{
    Normal = 1,
    Complementary = 2,
    Adjustment = 3,
    Return = 4,
}

/// <summary><c>indPres</c>.</summary>
public enum NfePresence
{
    NotApplicable = 0,
    InPerson = 1,
    Internet = 2,
    Phone = 3,
    HomeDelivery = 4,
    InPersonOutside = 9,
}

/// <summary><c>modFrete</c>.</summary>
public enum NfeFreightMode
{
    Issuer = 0,
    Recipient = 1,
    ThirdParty = 2,
    OwnByIssuer = 3,
    OwnByRecipient = 4,
    NoTransport = 9,
}

/// <summary><c>indIEDest</c>.</summary>
public enum NfeStateRegistration
{
    Taxpayer = 1,
    Exempt = 2,
    NonTaxpayer = 9,
}

/// <summary><c>tPag</c> (os usuais; outro código vai como número).</summary>
public enum NfePaymentMethod
{
    Cash = 1,
    CreditCard = 3,
    DebitCard = 4,
    Boleto = 15,
    BankDeposit = 16,
    PixDynamic = 17,
    NoPayment = 90,
    Other = 99,
}

/// <summary>
/// Pedido de NF-e com os tributos já calculados pelo BemAqui (FIS-02b): o Emissor transporta e assina, não calcula. Valores com 2
/// casas, quantidades com 4 e preço unitário com até 4 (o adaptador arredonda do mesmo jeito que o hash do payload, <see cref="NfePayload"/>).
/// </summary>
public sealed record NfeIssueRequest(
    string IssuerDocument,
    int Series,
    string OperationNature,
    NfeOperationType OperationType,
    NfePurpose Purpose,
    bool FinalConsumer,
    NfePresence Presence,
    NfeFreightMode FreightMode,
    NfeRecipient Recipient,
    IReadOnlyList<NfeItem> Items,
    IReadOnlyList<NfePayment> Payments,
    string? AdditionalInformation = null,
    string? TaxAuthorityInformation = null,
    IReadOnlyList<string>? ReferencedAccessKeys = null,
    DateTimeOffset? IssuedAt = null);

public sealed record NfeRecipient(string? Cnpj, string? Cpf, string Name, NfeStateRegistration StateRegistrationIndicator, string? StateRegistration, string? Email, NfeAddress Address);

public sealed record NfeAddress(string Street, string Number, string? Complement, string District, string CityCode, string CityName, string State, string PostalCode, string? Phone = null);

public sealed record NfeItem(
    string Code,
    string Description,
    string Ncm,
    string? Cest,
    string Cfop,
    string Unit,
    decimal Quantity,
    decimal UnitPrice,
    decimal Total,
    NfeItemTaxes Taxes,
    decimal Discount = 0,
    decimal Freight = 0,
    decimal Insurance = 0,
    decimal OtherCosts = 0,
    string? Gtin = null,
    bool ComposesTotal = true,
    string? CustomerOrderNumber = null);

public sealed record NfeItemTaxes(int Origin, NfeIcms Icms, NfePisCofins Pis, NfePisCofins Cofins, NfeIpi? Ipi = null, NfeIbsCbs? IbsCbs = null);

/// <summary>ICMS do item (CST ou CSOSN), com ST e FCP opcionais.</summary>
public sealed record NfeIcms(
    string Cst,
    decimal Base,
    decimal Rate,
    decimal Amount,
    int BaseMode = 3,
    decimal? BaseReduction = null,
    decimal? StBase = null,
    decimal? StRate = null,
    decimal? StAmount = null,
    decimal? StMargin = null,
    decimal? FcpBase = null,
    decimal? FcpRate = null,
    decimal? FcpAmount = null);

public sealed record NfePisCofins(string Cst, decimal Base, decimal Rate, decimal Amount);

public sealed record NfeIpi(string Cst, decimal Base, decimal Rate, decimal Amount, string LegalFramework = "999");

/// <summary>IBS e CBS (Reforma Tributária, P-03, lacuna L-b): CST e <c>cClassTrib</c> do BemAqui; base e alíquotas juntas ou nenhuma.</summary>
public sealed record NfeIbsCbs(string Cst, string Classification, decimal? Base = null, NfeTaxRate? StateIbs = null, NfeTaxRate? CityIbs = null, NfeTaxRate? Cbs = null);

public sealed record NfeTaxRate(decimal Rate, decimal Amount);

public sealed record NfePayment(NfePaymentMethod Method, decimal Amount);

// ---------- Respostas ----------

/// <summary>Documento no Emissor: resposta da emissão e da consulta.</summary>
public sealed record NfeDocument(
    Guid DocumentId,
    FiscalDocumentStatus Status,
    string? AccessKey,
    int Series,
    long Number,
    string? Protocol,
    DateTimeOffset? AuthorizedAt,
    FiscalRejection? Rejection,
    DateTimeOffset CreatedAt)
{
    public EmitterNfeState ToState() => new(DocumentId, Status, AccessKey, Series, Number, Protocol, AuthorizedAt, Rejection?.Code, Rejection?.Reason, Rejection?.SuggestedAction);
}

/// <summary>Rejeição traduzida pelo Emissor; <see cref="SuggestedAction"/> pode faltar em código desconhecido.</summary>
public sealed record FiscalRejection(int Code, string Reason, string? SuggestedAction);

/// <summary>Prévia: chave e número que a nota teria agora (pode mudar) e os avisos.</summary>
public sealed record NfePreview(string AccessKey, long Number, int Series, IReadOnlyList<string> Warnings);

public sealed record FiscalFile(byte[] Content, string ContentType, string FileName);

public enum NfeEventType
{
    Cancellation = 1,
    CorrectionLetter = 2,
    NumberRangeInvalidation = 3,
}

public sealed record NfeEventResult(Guid Id, NfeEventType Type, bool Accepted, string? Protocol, int? Code, string? Reason, string? SuggestedAction, DateTimeOffset RegisteredAt);

public sealed record NumberRangeInvalidation(string IssuerDocument, int Series, long FirstNumber, long LastNumber, string Justification);

public sealed record NumberRangeInvalidationResult(bool Accepted, int Series, long FirstNumber, long LastNumber, string? Protocol, int? Code, string? Reason, string? SuggestedAction);

public sealed record SefazStatus(bool Online, string State, string Environment, string? Message, int? AverageResponseMs, string? SuggestedContingency, DateTimeOffset CheckedAt);

/// <summary>O <c>GET /me</c>: a chave, o tenant do Emissor, os escopos efetivos e os emitentes.</summary>
/// <remarks>
/// <paramref name="NfseCities"/> (FIS-06, lacuna L-m): os municípios (IBGE) com a NFS-e habilitada no Emissor para esta chave. O Emissor de
/// hoje não informa (nulo): a NFS-e de Caçapava segue pelo processo provisório do portal da prefeitura (D5).
/// </remarks>
public sealed record EmitterAccount(Guid ClientId, string Name, Guid TenantId, IReadOnlyList<string> Scopes, bool LicenseBlocked, int? RateLimitPerMinute, IReadOnlyList<EmitterIssuer> Issuers,
    IReadOnlyList<string>? NfseCities = null);

public sealed record EmitterIssuer(string Document, string Name, string State, string CityCode, FiscalEnvironment Environment, bool Active, EmitterCertificate Certificate);

/// <summary>Só o que permite alertar o vencimento (ADR 0027 §3): nunca o arquivo.</summary>
public sealed record EmitterCertificate(bool Present, string? Thumbprint, string? Holder, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil, int? DaysRemaining, bool Expired);

public sealed record CertificateUploadResult(Guid Id, string Thumbprint, DateTimeOffset ValidUntil);

// ---------- NFS-e (FIS-06) ----------

public sealed record NfseIssueRequest(
    string IssuerDocument,
    string Series,
    NfeRecipient Taker,
    NfseService Service,
    string? Notes = null,
    string? LinkedNfeAccessKey = null);

public sealed record NfseService(
    string ServiceListItem,
    string MunicipalTaxCode,
    string NationalTaxCode,
    string Nbs,
    string Description,
    string ServiceCityCode,
    decimal ServicesAmount,
    decimal Base,
    decimal IssRate,
    decimal IssAmount,
    bool IssWithheld,
    decimal NetAmount,
    decimal? InssAmount = null,
    NfseIbsCbs? IbsCbs = null);

public sealed record NfseIbsCbs(string Cst, string Classification, string OperationCode);

public sealed record NfseDocument(Guid DocumentId, FiscalDocumentStatus Status, string? Number, string? VerificationCode, string? PrintLink, FiscalRejection? Rejection)
{
    /// <summary>Nos tipos da nota local (sem chave nem série da SEFAZ; o número é o da prefeitura).</summary>
    public EmitterNfeState ToState() => new(DocumentId, Status, null, 0, NfseRules.ParseNumber(Number) ?? 0, null, null, Rejection?.Code, Rejection?.Reason, Rejection?.SuggestedAction);
}
