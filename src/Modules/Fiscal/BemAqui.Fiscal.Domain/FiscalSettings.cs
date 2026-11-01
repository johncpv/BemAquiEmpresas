using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>Regime tributário da empresa (FIS-02a, P-01). O CRT da NF-e sai daqui (<see cref="FiscalSettings.CrtOf"/>).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaxRegime>))]
public enum TaxRegime
{
    SimplesNacional,
    SimplesNacionalExcess,
    Mei,
    LucroPresumido,
    LucroReal,
}

/// <summary>Onde o ISS da NFS-e é devido (LC 116): no município da obra (item 7.02) ou no da empresa (ex.: item 14.06).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<IssLocation>))]
public enum IssLocation
{
    WorkSite,
    Company,
}

/// <summary>Como sai a venda mista (D4): a NF-e do equipamento na expedição e a instalação em NFS-e no aceite técnico.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MixedSaleMode>))]
public enum MixedSaleMode
{
    EquipmentNfeServiceNfse,
}

/// <summary>Documento fiscal a emitir: define quais campos críticos a produção exige conferidos (CA 6).</summary>
public enum FiscalDocumentType
{
    Nfe,
    Nfse,
}

/// <summary>
/// Configuração fiscal do tenant (<c>fiscal.fiscal_settings</c>, FIS-02a, ADR 0042): uma linha por tenant (<see cref="Slot"/> único), nascida
/// do seed <c>fiscal-defaults</c> com os valores propostos e editada em Configurações → Fiscal. Cada valor tem uma chave em
/// <see cref="FiscalSettingKeys"/>; a conferência fica em <see cref="SettingConfirmation"/> (vale para o valor, pelo hash). IE, IM, endereço e
/// IBGE não ficam aqui (fonte única: <c>settings.company_profile</c>). Do certificado A1 só os metadados: o arquivo fica no cofre do
/// Emissor (D12). Colunas planas (e não jsonb) para validar, auditar campo a campo e usar <c>numeric(7,4)</c> nas alíquotas.
/// </summary>
[DocumentName("A configuração fiscal")]
public sealed class FiscalSettings : Entity, IAuditable, IConcurrencyVersioned
{
    public const int SingleSlot = 1;

    public int Slot { get; init; } = SingleSlot;

    // Regime e atividades
    public TaxRegime Regime { get; set; } = TaxRegime.LucroPresumido;

    /// <summary>Código de Regime Tributário da NF-e, sempre derivado do regime (1 Simples, 2 excesso de sublimite, 3 Normal, 4 MEI).</summary>
    public int Crt { get; private set; } = 3;

    public string CnaeMain { get; set; } = "4329103";

    public string[] CnaeSecondary { get; set; } = [];

    // Série e ambiente
    public int? NfeSeries { get; set; } = 2;

    public FiscalEnvironment Environment { get; set; } = FiscalEnvironment.Homologation;

    // Operações padrão (CFOP por cenário)
    public string CfopSaleInStateContributor { get; set; } = "5101";

    public string CfopSaleInStateFinal { get; set; } = "5101";

    public string CfopSaleInterstateContributor { get; set; } = "6101";

    public string CfopSaleInterstateFinal { get; set; } = "6107";

    public string CfopResaleInState { get; set; } = "5102";

    public string CfopResaleInterstate { get; set; } = "6102";

    public MixedSaleMode MixedSale { get; set; } = MixedSaleMode.EquipmentNfeServiceNfse;

    // CST padrão
    public string CstIcms { get; set; } = "00";

    public string CstPisCofins { get; set; } = "01";

    public string CstIpi { get; set; } = "50";

    // Alíquotas (%)
    public decimal IcmsInternalRate { get; set; } = 18m;

    public decimal IcmsInterstateSouthSoutheastRate { get; set; } = 12m;

    public decimal IcmsInterstateOtherRate { get; set; } = 7m;

    public decimal IcmsImportedRate { get; set; } = 4m;

    public decimal PisRate { get; set; } = 0.65m;

    public decimal CofinsRate { get; set; } = 3m;

    /// <summary>IPI pela TIPI do NCM de cada item (CAD-02a): não há alíquota única; a chave confere a regra.</summary>
    public bool IpiFromNcm { get; set; } = true;

    // Custo do projeto (gerencial)
    public string[] CostTaxes { get; set; } = [.. FiscalSettingsRules.DefaultCostTaxes];

    // Entrada (FIS-04b, P-01): tributos recuperáveis da NF-e de entrada; vazio = nenhum (ICMS, IPI, PIS e COFINS ficam no custo).
    public string[] InputTaxCredit { get; set; } = [];

    // NFS-e
    public string NfseServiceItem { get; set; } = "7.02";

    public string? NfseMunicipalCode { get; set; }

    public string? NfseCtn { get; set; }

    public string? NfseNbs { get; set; }

    public decimal NfseIssRate { get; set; } = 5m;

    public IssLocation NfseIssLocation { get; set; } = IssLocation.WorkSite;

    public bool NfseIssWithheld { get; set; }

    public bool NfseFederalWithholding { get; set; }

    // Reforma Tributária (IBS/CBS, P-03)
    public string IbsCbsCst { get; set; } = "000";

    public string IbsCbsClassification { get; set; } = "000001";

    public decimal CbsRate { get; set; } = 0.9m;

    /// <summary>IBS estadual (UF).</summary>
    public decimal IbsRate { get; set; } = 0.1m;

    /// <summary>IBS municipal (dado do Emissor em 27/10: 0% em 2026).</summary>
    public decimal IbsCityRate { get; set; }

    // Prazos
    public int CancellationWindowHours { get; set; } = 24;

    // Transporte e cobrança
    public int FreightMode { get; set; }

    public string VolumeSpecies { get; set; } = "VOLUME";

    public bool IncludeDuplicates { get; set; }

    // Certificado A1: só os metadados (D12). Na auditoria fica só "válido até" (CA 12 e 16).
    public DateTimeOffset? CertificateNotAfter { get; private set; }

    [NotAudited]
    public string? CertificateSubject { get; private set; }

    [NotAudited]
    public string? CertificateDocument { get; private set; }

    [NotAudited]
    public string? CertificateThumbprint { get; private set; }

    [NotAudited]
    public DateTimeOffset? CertificateUploadedAt { get; private set; }

    [NotAudited]
    public Guid? CertificateUploadedBy { get; private set; }

    /// <summary>Quando o <c>GET /me</c> do Emissor mostrou outro certificado que o guardado aqui (trocado fora do BemAqui, CA 13).</summary>
    [NotAudited]
    public DateTimeOffset? CertificateChangedOutsideAt { get; private set; }

    /// <summary>
    /// Data de corte fiscal (<c>project_tax_cutover_on</c>, ADR 0025 adendo e FIN-08): a data da primeira NF-e autorizada em produção, gravada
    /// pela própria autorização (FIS-03a). Daí em diante o imposto da nota entra no custo do projeto e a receita do DRE passa a ser pela nota.
    /// Homologação nunca grava. Não é campo da tela.
    /// </summary>
    public DateOnly? ProjectTaxCutoverOn { get; private set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public uint Version { get; set; }

    /// <summary>Primeira nota autorizada em produção (FIS-03a): grava a data de corte uma vez só; devolve a que vale.</summary>
    public DateOnly StartProjectTaxCutover(DateOnly issuedOn) => ProjectTaxCutoverOn ??= issuedOn;

    /// <summary>CRT do regime (CA 9 e tabela "Valores pré-configurados").</summary>
    public static int CrtOf(TaxRegime regime) => regime switch
    {
        TaxRegime.SimplesNacional => 1,
        TaxRegime.SimplesNacionalExcess => 2,
        TaxRegime.Mei => 4,
        _ => 3,
    };

    public void SyncCrt() => Crt = CrtOf(Regime);

    /// <summary>Certificado aceito pelo Emissor (CA 12): só validade, titular, CNPJ e thumbprint, quem e quando.</summary>
    public void RecordCertificate(string subject, string document, string thumbprint, DateTimeOffset notAfter, Guid? by, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrEmpty(thumbprint);
        CertificateSubject = subject.Length > 300 ? subject[..300] : subject;
        CertificateDocument = document;
        CertificateThumbprint = thumbprint.ToUpperInvariant();
        CertificateNotAfter = notAfter;
        CertificateUploadedAt = at;
        CertificateUploadedBy = by;
        CertificateChangedOutsideAt = null;
    }

    /// <summary>O Emissor mostra outro certificado (trocado fora do BemAqui, CA 13): os dados passam a ser os de lá.</summary>
    public void RecordOutsideChange(string? holder, string thumbprint, DateTimeOffset? notAfter, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrEmpty(thumbprint);
        CertificateSubject = holder is { Length: > 300 } ? holder[..300] : holder;
        CertificateThumbprint = thumbprint.ToUpperInvariant();
        CertificateNotAfter = notAfter;
        CertificateChangedOutsideAt = at;
    }

    /// <summary>Dias até o vencimento pelo dia de São Paulo (negativo: vencido).</summary>
    public int? CertificateDaysLeft(DateOnly today) =>
        CertificateNotAfter is { } notAfter ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(notAfter, SaoPaulo).DateTime).DayNumber - today.DayNumber : null;

    public bool CertificateExpired(DateTimeOffset now) => CertificateNotAfter is { } notAfter && notAfter <= now;

    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
}

/// <summary>
/// Conferência de um valor fiscal (<c>fiscal.setting_confirmations</c>, CA 5): quem conferiu, quando e o hash do valor conferido. Única
/// por chave; valor com outro hash volta a "A confirmar com a contadora" (a conferência vale para o valor, ADR 0042 §1).
/// </summary>
[DocumentName("A conferência fiscal")]
public sealed class SettingConfirmation : Entity, IAuditable
{
    public required string SettingKey { get; init; }

    public required string ValueHash { get; set; }

    public Guid? ConfirmedBy { get; set; }

    public DateTimeOffset ConfirmedAt { get; set; }
}

/// <summary>Histórico das trocas de certificado (<c>fiscal.certificate_uploads</c>, CA 13): só metadados, nunca o arquivo nem a senha.</summary>
public sealed class CertificateUpload : Entity
{
    public required string Thumbprint { get; init; }

    public string? Subject { get; init; }

    public string? Document { get; init; }

    public DateTimeOffset? NotAfter { get; init; }

    /// <summary><c>BemAqui</c> (enviado pela tela) ou <c>Emissor</c> (visto no <c>GET /me</c>, trocado fora).</summary>
    public required string Source { get; init; }

    public Guid? UploadedBy { get; init; }

    public DateTimeOffset At { get; init; }
}

/// <summary>Origem de uma troca de certificado no histórico.</summary>
public static class FiscalCertificateSources
{
    public const string BemAqui = "BemAqui";
    public const string Emissor = "Emissor";
}
