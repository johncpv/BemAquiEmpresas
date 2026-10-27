using System.Text.Json.Serialization;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application.Settings;

/// <summary>Os valores de Configurações → Fiscal, como a tela lê e grava (CA 3 e 4). O CRT não entra: sai do regime.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FiscalSettingsValues(
    TaxRegime Regime,
    string CnaeMain,
    IReadOnlyList<string> CnaeSecondary,
    int? NfeSeries,
    FiscalEnvironment Environment,
    string CfopSaleInStateContributor,
    string CfopSaleInStateFinal,
    string CfopSaleInterstateContributor,
    string CfopSaleInterstateFinal,
    string CfopResaleInState,
    string CfopResaleInterstate,
    MixedSaleMode MixedSale,
    string CstIcms,
    string CstPisCofins,
    string CstIpi,
    decimal IcmsInternalRate,
    decimal IcmsInterstateSouthSoutheastRate,
    decimal IcmsInterstateOtherRate,
    decimal IcmsImportedRate,
    decimal PisRate,
    decimal CofinsRate,
    bool IpiFromNcm,
    IReadOnlyList<string> CostTaxes,
    string NfseServiceItem,
    string? NfseMunicipalCode,
    string? NfseCtn,
    string? NfseNbs,
    decimal NfseIssRate,
    IssLocation NfseIssLocation,
    bool NfseIssWithheld,
    bool NfseFederalWithholding,
    string IbsCbsCst,
    string IbsCbsClassification,
    decimal CbsRate,
    decimal IbsRate,
    int CancellationWindowHours,
    int FreightMode,
    string VolumeSpecies,
    bool IncludeDuplicates);

/// <summary>"Salvar configuração fiscal" (CA 4, 8): <c>Code</c> só na troca para produção (2FA de novo); <c>ConfirmSeriesChange</c> depois do aviso da série.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveFiscalSettingsRequest(FiscalSettingsValues Values, string? Code = null, bool ConfirmSeriesChange = false);

/// <summary>"Marcar como conferido" de um campo ou de uma seção inteira (CA 5).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmFiscalSettingsRequest(IReadOnlyList<string> Keys);

/// <summary>Situação de um valor: "A confirmar com a contadora" (<c>ToConfirm</c>) ou conferido, com quem e quando.</summary>
public sealed record FiscalSettingReview(
    string Key, string Section, string Label, FiscalCriticality Critical, FiscalReviewState ReviewState, string? ReviewedByName, DateTimeOffset? ReviewedAt);

public sealed record FiscalSettingSectionView(string Code, string Title, int Total, int Pending, string? FirstPendingKey);

/// <summary>Dados da empresa, só leitura (CA 3): de <c>settings.company_profile</c> (CAD-06) e o CNPJ do catálogo.</summary>
public sealed record FiscalCompanyView(string LegalName, string? Document, string? StateRegistration, string? MunicipalRegistration, string? Address, string? CityIbge);

/// <summary>Pré-condição que bloqueia a emissão (CA 7): o que falta e onde resolver.</summary>
public sealed record FiscalPrecondition(string Key, string Message, string Link);

/// <summary>Resumo do certificado na tela fiscal (o detalhe fica na tela do certificado).</summary>
public sealed record FiscalCertificateSummary(DateTimeOffset? NotAfter, int? DaysLeft, string Status);

/// <summary>Configurações → Fiscal (CA 3): valores, conferência por campo e seção, empresa, pré-condições e quem pode editar.</summary>
public sealed record FiscalSettingsView(
    Guid? Id,
    FiscalSettingsValues Values,
    int Crt,
    IReadOnlyList<FiscalSettingReview> Reviews,
    IReadOnlyList<FiscalSettingSectionView> Sections,
    int ConfirmedCount,
    int TotalCount,
    bool FirstConfiguration,
    FiscalCompanyView Company,
    IReadOnlyList<FiscalPrecondition> Preconditions,
    FiscalCertificateSummary Certificate,
    string AllowedEnvironment,
    bool CanEdit,
    IReadOnlyList<string> PendingCriticalNfe);

/// <summary>Resultado do salvar: a configuração e, quando o regime muda, o aviso das regras a revisar (CA 9; as regras são da FIS-02b).</summary>
public sealed record FiscalSettingsSaved(FiscalSettingsView Settings, IReadOnlyList<string> ChangedKeys, string? Notice, IReadOnlyList<string> RulesToReview);

/// <summary>
/// Prontidão para emitir (CA 6, 7 e 14): em produção, a lista dos campos críticos não conferidos bloqueia; em homologação, só avisa
/// (<see cref="PendingKeys"/>).
/// </summary>
public sealed record FiscalReadiness(FiscalEnvironment Environment, IReadOnlyList<string> PendingKeys, IReadOnlyList<string> Warnings);

/// <summary>Códigos de erro de Configurações → Fiscal (contrato com o front e com a FIS-03a e a FIS-06).</summary>
public static class FiscalSettingsErrors
{
    public const string NotConfirmed = "fiscal_settings_not_confirmed";
    public const string Incomplete = "fiscal_settings_incomplete";
    public const string CertificateExpired = "fiscal_certificate_expired";
    public const string SeriesInUse = "fiscal_series_in_use";
    public const string TwoFactorRequired = "two_factor_required";
    public const string TwoFactorInvalid = "two_factor_invalid";
}

/// <summary>Valores padrão e prontidão do Fiscal (FIS-02a), para a emissão (FIS-03a) e a NFS-e (FIS-06).</summary>
public interface IFiscalSettings
{
    /// <summary>A configuração em uso (sem linha salva: os valores do seed <c>fiscal-defaults</c>).</summary>
    Task<FiscalSettings> CurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pode emitir? Pré-condição faltando: 409 <c>fiscal_settings_incomplete</c>; certificado vencido: 409 <c>fiscal_certificate_expired</c>;
    /// em produção, campo crítico do documento sem conferência: 409 <c>fiscal_settings_not_confirmed</c> com a lista e o link do primeiro.
    /// </summary>
    Task<Result<FiscalReadiness>> ReadinessAsync(FiscalDocumentType document, CancellationToken cancellationToken = default);
}
