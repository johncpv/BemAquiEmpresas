using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BemAqui.Fiscal.Domain;

/// <summary>Para qual documento o campo é crítico em produção (coluna "Crítico para produção" da FIS-02a).</summary>
public enum FiscalCriticality
{
    /// <summary>Gerencial ou operacional: nunca bloqueia (ex.: tributos no custo do projeto, ambiente).</summary>
    None,

    /// <summary>Crítico para a NF-e e para a NFS-e.</summary>
    All,

    Nfe,

    /// <summary>Crítico só para a NFS-e (FIS-06).</summary>
    Nfse,
}

/// <summary>Seção da tela Configurações → Fiscal (CA 3, "Telas e estados").</summary>
public sealed record FiscalSettingSection(string Code, string Title);

/// <summary>Um valor conferível: a chave (camelCase, igual ao campo da API), a seção, o rótulo, a criticidade e o valor canônico.</summary>
public sealed record FiscalSettingKey(string Key, string Section, string Label, FiscalCriticality Critical, Func<FiscalSettings, string> Canonical)
{
    public bool CriticalFor(FiscalDocumentType document) => Critical switch
    {
        FiscalCriticality.All => true,
        FiscalCriticality.Nfe => document == FiscalDocumentType.Nfe,
        FiscalCriticality.Nfse => document == FiscalDocumentType.Nfse,
        _ => false,
    };

    /// <summary>Hash do valor (SHA-256 da chave e do valor canônico): a conferência vale para este valor (CA 4 e 5).</summary>
    public string HashOf(FiscalSettings settings) => FiscalSettingKeys.Hash(Key, Canonical(settings));
}

/// <summary>
/// As chaves de Configurações → Fiscal e quais são críticas para a produção (FIS-02a, ADR 0042 §2). A lista é fonte única: a tela, a
/// conferência, o <c>ReadinessAsync</c> e o PDF para a contadora leem daqui (teste de unidade confere a cobertura).
/// </summary>
public static class FiscalSettingKeys
{
    public static readonly IReadOnlyList<FiscalSettingSection> Sections =
    [
        new("regime", "Regime e atividades"),
        new("serie", "Série e ambiente"),
        new("operacoes", "Operações padrão"),
        new("cst", "CST e alíquotas"),
        new("custo", "Custo do projeto"),
        new("nfse", "NFS-e"),
        new("ibs-cbs", "Reforma Tributária (IBS/CBS)"),
        new("transporte", "Transporte e cobrança"),
        new("prazos", "Prazos"),
    ];

    public static readonly IReadOnlyList<FiscalSettingKey> All =
    [
        new("regime", "regime", "Regime tributário", FiscalCriticality.All, s => s.Regime.ToString()),
        new("crt", "regime", "CRT (Código de Regime Tributário)", FiscalCriticality.All, s => Int(FiscalSettings.CrtOf(s.Regime))),
        new("cnaeMain", "regime", "CNAE principal", FiscalCriticality.All, s => s.CnaeMain),
        new("cnaeSecondary", "regime", "CNAEs secundários", FiscalCriticality.All, s => string.Join(',', s.CnaeSecondary)),
        new("nfeSeries", "serie", "Série exclusiva da NF-e", FiscalCriticality.Nfe, s => s.NfeSeries is { } n ? Int(n) : string.Empty),
        new("environment", "serie", "Ambiente", FiscalCriticality.None, s => s.Environment.ToString()),
        new("cfopSaleInStateContributor", "operacoes", "Venda de produção própria a contribuinte em SP", FiscalCriticality.Nfe, s => s.CfopSaleInStateContributor),
        new("cfopSaleInStateFinal", "operacoes", "Venda de produção própria a não contribuinte em SP", FiscalCriticality.Nfe, s => s.CfopSaleInStateFinal),
        new("cfopSaleInterstateContributor", "operacoes", "Venda de produção própria a contribuinte de outro estado", FiscalCriticality.Nfe, s => s.CfopSaleInterstateContributor),
        new("cfopSaleInterstateFinal", "operacoes", "Venda de produção própria a não contribuinte de outro estado", FiscalCriticality.Nfe, s => s.CfopSaleInterstateFinal),
        new("cfopResaleInState", "operacoes", "Revenda de peça em SP", FiscalCriticality.Nfe, s => s.CfopResaleInState),
        new("cfopResaleInterstate", "operacoes", "Revenda de peça para outro estado", FiscalCriticality.Nfe, s => s.CfopResaleInterstate),
        new("mixedSale", "operacoes", "Venda mista", FiscalCriticality.Nfe, s => s.MixedSale.ToString()),
        new("cstIcms", "cst", "CST do ICMS", FiscalCriticality.Nfe, s => s.CstIcms),
        new("cstPisCofins", "cst", "CST do PIS e da COFINS", FiscalCriticality.Nfe, s => s.CstPisCofins),
        new("cstIpi", "cst", "CST do IPI", FiscalCriticality.Nfe, s => s.CstIpi),
        new("icmsInternalRate", "cst", "ICMS interno SP", FiscalCriticality.Nfe, s => Dec(s.IcmsInternalRate)),
        new("icmsInterstateSouthSoutheastRate", "cst", "ICMS interestadual (Sul e Sudeste, exceto ES)", FiscalCriticality.Nfe, s => Dec(s.IcmsInterstateSouthSoutheastRate)),
        new("icmsInterstateOtherRate", "cst", "ICMS interestadual (Norte, Nordeste, Centro-Oeste e ES)", FiscalCriticality.Nfe, s => Dec(s.IcmsInterstateOtherRate)),
        new("icmsImportedRate", "cst", "ICMS interestadual de mercadoria importada", FiscalCriticality.Nfe, s => Dec(s.IcmsImportedRate)),
        new("pisRate", "cst", "PIS", FiscalCriticality.All, s => Dec(s.PisRate)),
        new("cofinsRate", "cst", "COFINS", FiscalCriticality.All, s => Dec(s.CofinsRate)),
        new("ipiFromNcm", "cst", "IPI pela TIPI do NCM do item", FiscalCriticality.Nfe, s => Bool(s.IpiFromNcm)),
        new("costTaxes", "custo", "Tributos que entram no custo do projeto", FiscalCriticality.None, s => string.Join(',', s.CostTaxes.Order(StringComparer.Ordinal))),
        new("nfseServiceItem", "nfse", "Item da LC 116", FiscalCriticality.Nfse, s => s.NfseServiceItem),
        new("nfseMunicipalCode", "nfse", "Código municipal do serviço (CodSrv)", FiscalCriticality.Nfse, s => s.NfseMunicipalCode ?? string.Empty),
        new("nfseCtn", "nfse", "Código de tributação nacional (CTN)", FiscalCriticality.Nfse, s => s.NfseCtn ?? string.Empty),
        new("nfseNbs", "nfse", "NBS", FiscalCriticality.Nfse, s => s.NfseNbs ?? string.Empty),
        new("nfseIssRate", "nfse", "Alíquota do ISS", FiscalCriticality.Nfse, s => Dec(s.NfseIssRate)),
        new("nfseIssLocation", "nfse", "Local de incidência do ISS", FiscalCriticality.Nfse, s => s.NfseIssLocation.ToString()),
        new("nfseIssWithheld", "nfse", "ISS retido pelo tomador", FiscalCriticality.Nfse, s => Bool(s.NfseIssWithheld)),
        new("nfseFederalWithholding", "nfse", "Retenções federais", FiscalCriticality.Nfse, s => Bool(s.NfseFederalWithholding)),
        new("ibsCbsCst", "ibs-cbs", "CST do IBS/CBS", FiscalCriticality.All, s => s.IbsCbsCst),
        new("ibsCbsClassification", "ibs-cbs", "Classificação tributária (cClassTrib)", FiscalCriticality.All, s => s.IbsCbsClassification),
        new("cbsRate", "ibs-cbs", "Alíquota da CBS", FiscalCriticality.All, s => Dec(s.CbsRate)),
        new("ibsRate", "ibs-cbs", "Alíquota do IBS estadual", FiscalCriticality.All, s => Dec(s.IbsRate)),
        new("ibsCityRate", "ibs-cbs", "Alíquota do IBS municipal", FiscalCriticality.All, s => Dec(s.IbsCityRate)),
        new("freightMode", "transporte", "Modalidade do frete", FiscalCriticality.Nfe, s => Int(s.FreightMode)),
        new("volumeSpecies", "transporte", "Espécie dos volumes", FiscalCriticality.Nfe, s => s.VolumeSpecies),
        new("includeDuplicates", "transporte", "Duplicatas na NF-e", FiscalCriticality.Nfe, s => Bool(s.IncludeDuplicates)),
        new("cancellationWindowHours", "prazos", "Prazo de cancelamento da NF-e (horas)", FiscalCriticality.Nfe, s => Int(s.CancellationWindowHours)),
    ];

    private static readonly Dictionary<string, FiscalSettingKey> ByKey = All.ToDictionary(k => k.Key, StringComparer.Ordinal);

    public static FiscalSettingKey? Find(string key) => ByKey.GetValueOrDefault(key);

    public static IEnumerable<FiscalSettingKey> CriticalFor(FiscalDocumentType document) => All.Where(k => k.CriticalFor(document));

    public static string Hash(string key, string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key + "\n" + canonical)));

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Dec(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";
}

/// <summary>Validações do "Salvar configuração fiscal" (CA 4) e listas fixas de valores aceitos.</summary>
public static partial class FiscalSettingsRules
{
    public const decimal MaxRate = 100m;
    public const int MaxSeries = 999;
    public const int MinCancellationHours = 1;
    public const int MaxCancellationHours = 168;

    public static readonly string[] CostTaxOptions = ["ICMS", "IPI", "PIS", "COFINS", "ISS", "IBS_CBS"];

    /// <summary>ICMS, PIS, COFINS, ISS e IBS/CBS; IPI fora (ponto 41, ADR 0025 adendo).</summary>
    public static readonly string[] DefaultCostTaxes = ["ICMS", "PIS", "COFINS", "ISS", "IBS_CBS"];

    /// <summary>CST do ICMS no Regime Normal (CRT 3).</summary>
    public static readonly string[] IcmsCstNormal = ["00", "02", "10", "15", "20", "30", "40", "41", "50", "51", "53", "60", "61", "70", "90"];

    /// <summary>CSOSN do Simples Nacional (CRT 1, 2 e 4).</summary>
    public static readonly string[] IcmsCsosn = ["101", "102", "103", "201", "202", "203", "300", "400", "500", "900"];

    public static readonly string[] PisCofinsCst =
        ["01", "02", "03", "04", "05", "06", "07", "08", "09", "49", "50", "51", "52", "53", "54", "55", "56", "60", "61", "62", "63", "64", "65", "66", "67", "70", "71", "72", "73", "74", "75", "98", "99"];

    /// <summary>CST do IPI de saída.</summary>
    public static readonly string[] IpiCstOutbound = ["50", "51", "52", "53", "54", "55", "99"];

    /// <summary>Modalidade do frete da NF-e (<c>modFrete</c>).</summary>
    public static readonly int[] FreightModes = [0, 1, 2, 3, 4, 9];

    public static IReadOnlyList<string> IcmsCstFor(int crt) => crt == 3 ? IcmsCstNormal : IcmsCsosn;

    /// <summary>Erros por campo (chave camelCase de <see cref="FiscalSettingKeys"/>), em PT-BR; vazio quando está tudo certo.</summary>
    public static Dictionary<string, string[]> Validate(FiscalSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string key, string message) => errors.TryAdd(key, [message]);

        if (!Enum.IsDefined(s.Regime))
        {
            Add("regime", "Escolha o regime tributário.");
        }

        if (!Cnae().IsMatch(s.CnaeMain))
        {
            Add("cnaeMain", "O CNAE tem 7 dígitos (ex.: 4329-1/03).");
        }

        if (s.CnaeSecondary.Length > 20 || s.CnaeSecondary.Any(c => !Cnae().IsMatch(c)))
        {
            Add("cnaeSecondary", "Cada CNAE secundário tem 7 dígitos (ex.: 2822-4/01), até 20.");
        }

        if (s.NfeSeries is { } series && series is < 1 or > MaxSeries)
        {
            Add("nfeSeries", "A série vai de 1 a 999.");
        }

        if (!Enum.IsDefined(s.Environment))
        {
            Add("environment", "Escolha o ambiente.");
        }

        Cfop(errors, "cfopSaleInStateContributor", s.CfopSaleInStateContributor, '5');
        Cfop(errors, "cfopSaleInStateFinal", s.CfopSaleInStateFinal, '5');
        Cfop(errors, "cfopSaleInterstateContributor", s.CfopSaleInterstateContributor, '6');
        Cfop(errors, "cfopSaleInterstateFinal", s.CfopSaleInterstateFinal, '6');
        Cfop(errors, "cfopResaleInState", s.CfopResaleInState, '5');
        Cfop(errors, "cfopResaleInterstate", s.CfopResaleInterstate, '6');
        if (!Enum.IsDefined(s.MixedSale))
        {
            Add("mixedSale", "Escolha como sai a venda mista.");
        }

        var crt = FiscalSettings.CrtOf(s.Regime);
        if (!IcmsCstFor(crt).Contains(s.CstIcms, StringComparer.Ordinal))
        {
            Add("cstIcms", crt == 3 ? "Use um CST do ICMS do Regime Normal (ex.: 00)." : "No Simples Nacional, use um CSOSN (ex.: 101 ou 102).");
        }

        if (!PisCofinsCst.Contains(s.CstPisCofins, StringComparer.Ordinal))
        {
            Add("cstPisCofins", "Use um CST do PIS e da COFINS da tabela (ex.: 01).");
        }

        if (!IpiCstOutbound.Contains(s.CstIpi, StringComparer.Ordinal))
        {
            Add("cstIpi", "Use um CST do IPI de saída (50 a 55 ou 99).");
        }

        Rate(errors, "icmsInternalRate", s.IcmsInternalRate);
        Rate(errors, "icmsInterstateSouthSoutheastRate", s.IcmsInterstateSouthSoutheastRate);
        Rate(errors, "icmsInterstateOtherRate", s.IcmsInterstateOtherRate);
        Rate(errors, "icmsImportedRate", s.IcmsImportedRate);
        Rate(errors, "pisRate", s.PisRate);
        Rate(errors, "cofinsRate", s.CofinsRate);
        Rate(errors, "nfseIssRate", s.NfseIssRate);
        Rate(errors, "cbsRate", s.CbsRate);
        Rate(errors, "ibsRate", s.IbsRate);
        Rate(errors, "ibsCityRate", s.IbsCityRate);

        if (s.CostTaxes.Any(t => !CostTaxOptions.Contains(t, StringComparer.Ordinal)) || s.CostTaxes.Distinct(StringComparer.Ordinal).Count() != s.CostTaxes.Length)
        {
            Add("costTaxes", "Escolha os tributos da lista.");
        }

        if (!ServiceItem().IsMatch(s.NfseServiceItem))
        {
            Add("nfseServiceItem", "O item da LC 116 tem o formato 7.02 ou 14.06.");
        }

        if (s.NfseMunicipalCode is { } code && !MunicipalCode().IsMatch(code))
        {
            Add("nfseMunicipalCode", "O código municipal tem o formato NN.NN.NN (ex.: 07.02.01).");
        }

        if (s.NfseCtn is { } ctn && !ShortCode().IsMatch(ctn))
        {
            Add("nfseCtn", "O CTN tem só números e pontos, até 20 caracteres.");
        }

        if (s.NfseNbs is { } nbs && !ShortCode().IsMatch(nbs))
        {
            Add("nfseNbs", "A NBS tem só números e pontos, até 20 caracteres.");
        }

        if (!Enum.IsDefined(s.NfseIssLocation))
        {
            Add("nfseIssLocation", "Escolha onde o ISS é devido.");
        }

        if (!IbsCbsCst().IsMatch(s.IbsCbsCst))
        {
            Add("ibsCbsCst", "O CST do IBS/CBS tem 3 dígitos (ex.: 000).");
        }

        if (!IbsCbsClass().IsMatch(s.IbsCbsClassification))
        {
            Add("ibsCbsClassification", "O cClassTrib tem 6 dígitos (ex.: 000001).");
        }

        if (s.CancellationWindowHours is < MinCancellationHours or > MaxCancellationHours)
        {
            Add("cancellationWindowHours", "O prazo de cancelamento vai de 1 a 168 horas.");
        }

        if (!FreightModes.Contains(s.FreightMode))
        {
            Add("freightMode", "Escolha a modalidade do frete.");
        }

        if (string.IsNullOrWhiteSpace(s.VolumeSpecies) || s.VolumeSpecies.Length > 60)
        {
            Add("volumeSpecies", "Informe a espécie dos volumes (até 60 caracteres).");
        }

        return errors;
    }

    /// <summary>Só os dígitos (CNAE digitado com pontuação).</summary>
    public static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    private static void Cfop(Dictionary<string, string[]> errors, string key, string value, char first)
    {
        if (value.Length != 4 || !value.All(char.IsAsciiDigit) || value[0] != first)
        {
            errors.TryAdd(key, [first == '5' ? "Use um CFOP de 4 dígitos começando por 5 (dentro do estado)." : "Use um CFOP de 4 dígitos começando por 6 (fora do estado)."]);
        }
    }

    private static void Rate(Dictionary<string, string[]> errors, string key, decimal value)
    {
        if (value is < 0 or > MaxRate || decimal.Round(value, 4) != value)
        {
            errors.TryAdd(key, ["A alíquota vai de 0% a 100%, com até 4 casas."]);
        }
    }

    [GeneratedRegex("^[0-9]{7}$")]
    private static partial Regex Cnae();

    [GeneratedRegex("^[0-9]{1,2}\\.[0-9]{2}$")]
    private static partial Regex ServiceItem();

    [GeneratedRegex("^[0-9]{2}\\.[0-9]{2}\\.[0-9]{2}$")]
    private static partial Regex MunicipalCode();

    [GeneratedRegex("^[0-9.]{1,20}$")]
    private static partial Regex ShortCode();

    [GeneratedRegex("^[0-9]{3}$")]
    private static partial Regex IbsCbsCst();

    [GeneratedRegex("^[0-9]{6}$")]
    private static partial Regex IbsCbsClass();
}

/// <summary>Selo de um valor (ADR 0042 §1): "A confirmar com a contadora" ou conferido.</summary>
public enum FiscalReviewState
{
    ToConfirm,
    Confirmed,
}
