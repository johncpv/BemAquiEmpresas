using System.Globalization;
using BemAqui.Fiscal.Domain;

namespace BemAqui.Fiscal.Application.Settings;

/// <summary>Valores da API ↔ entidade, com a normalização do salvar (CNAE só dígitos, textos aparados, vazios como nulos).</summary>
public static class FiscalSettingsMapping
{
    public static FiscalSettingsValues ToValues(FiscalSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new FiscalSettingsValues(
            s.Regime, s.CnaeMain, s.CnaeSecondary, s.NfeSeries, s.Environment,
            s.CfopSaleInStateContributor, s.CfopSaleInStateFinal, s.CfopSaleInterstateContributor, s.CfopSaleInterstateFinal, s.CfopResaleInState, s.CfopResaleInterstate,
            s.MixedSale, s.CstIcms, s.CstPisCofins, s.CstIpi,
            s.IcmsInternalRate, s.IcmsInterstateSouthSoutheastRate, s.IcmsInterstateOtherRate, s.IcmsImportedRate, s.PisRate, s.CofinsRate, s.IpiFromNcm,
            s.CostTaxes, s.NfseServiceItem, s.NfseMunicipalCode, s.NfseCtn, s.NfseNbs, s.NfseIssRate, s.NfseIssLocation, s.NfseIssWithheld, s.NfseFederalWithholding,
            s.IbsCbsCst, s.IbsCbsClassification, s.CbsRate, s.IbsRate, s.IbsCityRate, s.CancellationWindowHours, s.FreightMode, s.VolumeSpecies, s.IncludeDuplicates);
    }

    /// <summary>Aplica os valores (o ambiente fica de fora: a troca tem regra própria, CA 8).</summary>
    public static void Apply(FiscalSettings s, FiscalSettingsValues v)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(v);
        s.Regime = v.Regime;
        s.SyncCrt();
        s.CnaeMain = FiscalSettingsRules.Digits(v.CnaeMain);
        s.CnaeSecondary = [.. (v.CnaeSecondary ?? []).Select(FiscalSettingsRules.Digits).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal)];
        s.NfeSeries = v.NfeSeries;
        s.CfopSaleInStateContributor = Text(v.CfopSaleInStateContributor);
        s.CfopSaleInStateFinal = Text(v.CfopSaleInStateFinal);
        s.CfopSaleInterstateContributor = Text(v.CfopSaleInterstateContributor);
        s.CfopSaleInterstateFinal = Text(v.CfopSaleInterstateFinal);
        s.CfopResaleInState = Text(v.CfopResaleInState);
        s.CfopResaleInterstate = Text(v.CfopResaleInterstate);
        s.MixedSale = v.MixedSale;
        s.CstIcms = Text(v.CstIcms);
        s.CstPisCofins = Text(v.CstPisCofins);
        s.CstIpi = Text(v.CstIpi);
        s.IcmsInternalRate = v.IcmsInternalRate;
        s.IcmsInterstateSouthSoutheastRate = v.IcmsInterstateSouthSoutheastRate;
        s.IcmsInterstateOtherRate = v.IcmsInterstateOtherRate;
        s.IcmsImportedRate = v.IcmsImportedRate;
        s.PisRate = v.PisRate;
        s.CofinsRate = v.CofinsRate;
        s.IpiFromNcm = v.IpiFromNcm;
        s.CostTaxes = [.. (v.CostTaxes ?? []).Select(t => Text(t).ToUpperInvariant())];
        s.NfseServiceItem = Text(v.NfseServiceItem);
        s.NfseMunicipalCode = Blank(v.NfseMunicipalCode);
        s.NfseCtn = Blank(v.NfseCtn);
        s.NfseNbs = Blank(v.NfseNbs);
        s.NfseIssRate = v.NfseIssRate;
        s.NfseIssLocation = v.NfseIssLocation;
        s.NfseIssWithheld = v.NfseIssWithheld;
        s.NfseFederalWithholding = v.NfseFederalWithholding;
        s.IbsCbsCst = Text(v.IbsCbsCst);
        s.IbsCbsClassification = Text(v.IbsCbsClassification);
        s.CbsRate = v.CbsRate;
        s.IbsRate = v.IbsRate;
        s.IbsCityRate = v.IbsCityRate;
        s.CancellationWindowHours = v.CancellationWindowHours;
        s.FreightMode = v.FreightMode;
        s.VolumeSpecies = Text(v.VolumeSpecies).ToUpperInvariant();
        s.IncludeDuplicates = v.IncludeDuplicates;
    }

    /// <summary>Valor como a contadora lê (PDF, CA 10).</summary>
    public static string Display(FiscalSettingKey key, FiscalSettings s)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(s);
        return key.Key switch
        {
            "regime" => RegimeLabel(s.Regime),
            "crt" => CrtLabel(FiscalSettings.CrtOf(s.Regime)),
            "cnaeMain" => Cnae(s.CnaeMain),
            "cnaeSecondary" => s.CnaeSecondary.Length == 0 ? "Nenhum" : string.Join(", ", s.CnaeSecondary.Select(Cnae)),
            "nfeSeries" => s.NfeSeries?.ToString(CultureInfo.InvariantCulture) ?? "Não informada",
            "environment" => s.Environment == FiscalEnvironment.Production ? "Produção" : "Homologação",
            "mixedSale" => "NF-e de venda do equipamento na expedição, sem remessa para a obra (endereço da obra nas informações complementares); instalação em NFS-e no aceite técnico",
            "cfopSaleInterstateFinal" => s.CfopSaleInterstateFinal + " (DIFAL bloqueado: a emissão recusa)",
            "ipiFromNcm" => s.IpiFromNcm ? "Pela TIPI do NCM de cada item" : "Não usar a TIPI",
            "costTaxes" => s.CostTaxes.Length == 0 ? "Nenhum" : string.Join(", ", s.CostTaxes.Select(t => t == "IBS_CBS" ? "IBS/CBS" : t)),
            "nfseMunicipalCode" or "nfseCtn" or "nfseNbs" => key.Canonical(s) is { Length: > 0 } code ? code : "A informar",
            "nfseIssLocation" => s.NfseIssLocation == IssLocation.WorkSite ? "Município da obra" : "Município da empresa",
            "nfseIssWithheld" => s.NfseIssWithheld ? "Sim" : "Não",
            "nfseFederalWithholding" => s.NfseFederalWithholding ? "Sim" : "Nenhuma",
            "freightMode" => FreightLabel(s.FreightMode),
            "includeDuplicates" => s.IncludeDuplicates ? "Sim" : "Não",
            "cancellationWindowHours" => s.CancellationWindowHours.ToString(CultureInfo.InvariantCulture) + " h da autorização",
            _ when key.Key.EndsWith("Rate", StringComparison.Ordinal) => decimal.Parse(key.Canonical(s), CultureInfo.InvariantCulture).ToString("0.####", PtBr) + "%",
            _ => key.Canonical(s),
        };
    }

    public static string RegimeLabel(TaxRegime regime) => regime switch
    {
        TaxRegime.SimplesNacional => "Simples Nacional",
        TaxRegime.SimplesNacionalExcess => "Simples Nacional, excesso de sublimite",
        TaxRegime.Mei => "MEI",
        TaxRegime.LucroReal => "Lucro Real",
        _ => "Lucro Presumido",
    };

    public static string CrtLabel(int crt) => crt switch
    {
        1 => "1 – Simples Nacional",
        2 => "2 – Simples Nacional, excesso de sublimite",
        4 => "4 – MEI",
        _ => "3 – Regime Normal",
    };

    public static string FreightLabel(int mode) => mode switch
    {
        0 => "0 – Por conta do remetente (CIF)",
        1 => "1 – Por conta do destinatário (FOB)",
        2 => "2 – Por conta de terceiros",
        3 => "3 – Próprio, por conta do remetente",
        4 => "4 – Próprio, por conta do destinatário",
        _ => "9 – Sem transporte",
    };

    public static string Cnae(string digits) => digits.Length == 7 ? $"{digits[..4]}-{digits[4]}/{digits[5..]}" : digits;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static string Text(string? value) => value?.Trim() ?? string.Empty;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
