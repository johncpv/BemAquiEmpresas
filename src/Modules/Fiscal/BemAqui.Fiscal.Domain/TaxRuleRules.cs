using System.Text.RegularExpressions;

namespace BemAqui.Fiscal.Domain;

/// <summary>
/// Regras sem banco das regras fiscais (FIS-02b): validação do "Salvar regra fiscal" (CA 2), sobreposição (CA 5), quais valores de
/// Configurações → Fiscal cada operação usa (aviso de revisão, CA 6), rótulos e a sugestão do CFOP fora do estado.
/// </summary>
public static partial class TaxRuleRules
{
    public const string OverlapCode = "tax_rule_overlap";
    public const string NoRuleCode = "tax_rule_not_found";
    public const string SegregationCode = "segregation_of_duties";
    public const string SegregationMessage = "Quem editou a regra não a ativa.";
    public const string ReviewNotice = "Os valores padrão mudaram em Configurações → Fiscal. Revise esta regra.";

    /// <summary>As 27 UFs.</summary>
    public static readonly string[] States =
        ["AC", "AL", "AM", "AP", "BA", "CE", "DF", "ES", "GO", "MA", "MG", "MS", "MT", "PA", "PB", "PE", "PI", "PR", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "SP", "TO"];

    /// <summary>Sul e Sudeste, exceto o ES (Resolução do Senado 22/1989): daqui para N, NE, CO e ES a alíquota interestadual é 7%.</summary>
    public static readonly string[] SouthSoutheastExceptEs = ["MG", "PR", "RJ", "RS", "SC", "SP"];

    /// <summary>Origem de mercadoria importada (Resolução do Senado 13/2012: 4% no interestadual): 1, 2, 3 e 8.</summary>
    public static readonly int[] ImportedOrigins = [1, 2, 3, 8];

    public static string Label(TaxOperation operation) => operation switch
    {
        TaxOperation.OwnProductionSale => "Venda de produção própria",
        TaxOperation.Resale => "Revenda",
        TaxOperation.ServiceProvision => "Prestação de serviço",
        TaxOperation.ShipmentToSite => "Remessa para obra",
        TaxOperation.ReturnFromSite => "Retorno de obra",
        TaxOperation.PurchaseReturn => "Devolução de compra",
        _ => "Outras saídas",
    };

    public static string Label(RecipientType recipient) => recipient switch
    {
        RecipientType.Contributor => "contribuinte",
        RecipientType.Exempt => "isento",
        _ => "não contribuinte",
    };

    public static bool IsService(TaxOperation operation) => operation == TaxOperation.ServiceProvision;

    /// <summary>CFOP fora do estado sugerido a partir do de dentro (5xxx → 6xxx), para o "Confirme" da tela.</summary>
    public static string? SuggestInterstateCfop(string? internalCfop) =>
        internalCfop is { Length: 4 } c && c[0] == '5' && c.All(char.IsAsciiDigit) ? "6" + c[1..] : null;

    /// <summary>Erros por campo (chaves camelCase, com o tributo: <c>icms.internalRate</c>), em PT-BR; vazio quando está tudo certo.</summary>
    public static Dictionary<string, string[]> Validate(TaxRule r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string key, string message) => errors.TryAdd(key, [message]);

        if (!Enum.IsDefined(r.Operation))
        {
            Add("operation", "Escolha a operação.");
        }

        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > TaxRule.NameMaxLength)
        {
            Add("name", "Informe a descrição da regra (até 120 caracteres).");
        }

        if (string.IsNullOrWhiteSpace(r.NatureOfOperation) || r.NatureOfOperation.Length > TaxRule.NatureMaxLength)
        {
            Add("natureOfOperation", "Informe a natureza da operação, o texto que vai na nota (até 60 caracteres).");
        }

        if (r.ValidTo is { } to && to < r.ValidFrom)
        {
            Add("validTo", "O fim da vigência vem depois do início.");
        }

        var service = IsService(r.Operation);
        if (service)
        {
            if (r.CfopInternal is not null || r.CfopInterstate is not null || r.Icms is not null || r.IcmsSt is not null || r.Fcp is not null || r.Ipi is not null || r.Difal is not null)
            {
                Add("operation", "A prestação de serviço sai em NFS-e: sem CFOP, ICMS, IPI nem DIFAL.");
            }

            if (r.Iss is null)
            {
                Add("iss", "Informe o ISS da prestação de serviço.");
            }
        }
        else
        {
            Cfop(errors, "cfopInternal", r.CfopInternal, '5');
            Cfop(errors, "cfopInterstate", r.CfopInterstate, '6');
            if (r.Iss is not null)
            {
                Add("iss", "ISS só na prestação de serviço.");
            }

            if (r.Icms is null)
            {
                Add("icms", "Informe o ICMS da operação.");
            }
        }

        if (r.Icms is { } icms)
        {
            if (!FiscalSettingsRules.IcmsCstFor(r.CrtScope).Contains(icms.Cst, StringComparer.Ordinal))
            {
                Add("icms.cst", r.CrtScope == 3 ? "Use um CST do ICMS do Regime Normal (ex.: 00)." : "No Simples Nacional, use um CSOSN (ex.: 101 ou 102).");
            }

            if (icms.BaseModality is < 0 or > 3)
            {
                Add("icms.baseModality", "Escolha a modalidade da base do ICMS.");
            }

            Rate(errors, "icms.internalRate", icms.InternalRate);
            Rate(errors, "icms.interstateSouthSoutheastRate", icms.InterstateSouthSoutheastRate);
            Rate(errors, "icms.interstateOtherRate", icms.InterstateOtherRate);
            Rate(errors, "icms.importedRate", icms.ImportedRate);
            Rate(errors, "icms.baseReduction", icms.BaseReduction);
        }

        if (r.IcmsSt is { } st)
        {
            if (st.BaseModality is < 0 or > 6)
            {
                Add("icmsSt.baseModality", "Escolha a modalidade da base do ICMS-ST.");
            }

            if (st.Mva is < 0 or > 1000 || decimal.Round(st.Mva, 4) != st.Mva)
            {
                Add("icmsSt.mva", "A MVA vai de 0% a 1000%, com até 4 casas.");
            }

            Rate(errors, "icmsSt.internalRate", st.InternalRate);
            Rate(errors, "icmsSt.baseReduction", st.BaseReduction);
        }

        if (r.Fcp is { } fcp)
        {
            Rate(errors, "fcp.rate", fcp.Rate);
        }

        if (r.Ipi is { } ipi)
        {
            if (!FiscalSettingsRules.IpiCstOutbound.Contains(ipi.Cst, StringComparer.Ordinal))
            {
                Add("ipi.cst", "Use um CST do IPI de saída (50 a 55 ou 99).");
            }

            if (!Framework().IsMatch(ipi.Framework))
            {
                Add("ipi.framework", "O enquadramento do IPI tem 3 dígitos (ex.: 999).");
            }

            Rate(errors, "ipi.rate", ipi.Rate);
        }

        PisCofins(errors, "pis", r.Pis, "PIS");
        PisCofins(errors, "cofins", r.Cofins, "COFINS");

        if (r.Iss is { } iss)
        {
            if (!ServiceItem().IsMatch(iss.ServiceItem))
            {
                Add("iss.serviceItem", "O item da LC 116 tem o formato 7.02 ou 14.06.");
            }

            if (!Enum.IsDefined(iss.Location))
            {
                Add("iss.location", "Escolha onde o ISS é devido.");
            }

            Rate(errors, "iss.rate", iss.Rate);
        }

        if (r.IbsCbs is { } ibs)
        {
            if (!IbsCbsCst().IsMatch(ibs.Cst))
            {
                Add("ibsCbs.cst", "O CST do IBS/CBS tem 3 dígitos (ex.: 000).");
            }

            if (!IbsCbsClass().IsMatch(ibs.Classification))
            {
                Add("ibsCbs.classification", "O cClassTrib tem 6 dígitos (ex.: 000001).");
            }

            Rate(errors, "ibsCbs.cbsRate", ibs.CbsRate);
            Rate(errors, "ibsCbs.ibsStateRate", ibs.IbsStateRate);
            Rate(errors, "ibsCbs.ibsCityRate", ibs.IbsCityRate);
        }

        if (r.Difal is { } difal)
        {
            Rate(errors, "difal.destinationInternalRate", difal.DestinationInternalRate);
        }

        if (r.CostTaxes.Any(t => !FiscalSettingsRules.CostTaxOptions.Contains(t, StringComparer.Ordinal)) || r.CostTaxes.Distinct(StringComparer.Ordinal).Count() != r.CostTaxes.Length)
        {
            Add("costTaxes", "Escolha os tributos da lista.");
        }

        if (r.ComplementaryInfo is { Length: > TaxRule.InfoMaxLength })
        {
            Add("complementaryInfo", "A informação complementar tem até 2000 caracteres.");
        }

        var c = r.Conditions;
        if (c.States.Any(s => !States.Contains(s, StringComparer.Ordinal)) || c.States.Count != c.States.Distinct(StringComparer.Ordinal).Count())
        {
            Add("conditions.states", "Escolha as UFs da lista.");
        }

        if (c.RecipientTypes.Any(t => !Enum.IsDefined(t)))
        {
            Add("conditions.recipientTypes", "Escolha os tipos de destinatário da lista.");
        }

        if (c.NcmPrefixes.Count > 50 || c.NcmPrefixes.Any(p => !NcmPrefix().IsMatch(p)))
        {
            Add("conditions.ncmPrefixes", "Cada NCM ou prefixo tem de 2 a 8 dígitos, até 50.");
        }

        return errors;
    }

    /// <summary>
    /// Sobreposição (CA 5): mesma operação e regime, vigências que se cruzam, UFs e tipos de destinatário em comum e o mesmo alcance de NCM.
    /// A mais específica por NCM é aceita quando a outra não tem NCM; duas sem NCM, ou com prefixos que se cobrem, se sobrepõem.
    /// </summary>
    public static bool Overlaps(TaxRule a, TaxRule b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.RuleId == b.RuleId || a.Operation != b.Operation || a.CrtScope != b.CrtScope)
        {
            return false;
        }

        var aEnd = a.ValidTo ?? DateOnly.MaxValue;
        var bEnd = b.ValidTo ?? DateOnly.MaxValue;
        if (a.ValidFrom > bEnd || b.ValidFrom > aEnd)
        {
            return false;
        }

        if (!Intersect(a.Conditions.States, b.Conditions.States) || !Intersect(a.Conditions.RecipientTypes, b.Conditions.RecipientTypes))
        {
            return false;
        }

        var (an, bn) = (a.Conditions.NcmPrefixes, b.Conditions.NcmPrefixes);
        if (an.Count == 0 && bn.Count == 0)
        {
            return true;
        }

        if (an.Count == 0 || bn.Count == 0)
        {
            return false;
        }

        return an.Any(x => bn.Any(y => x.StartsWith(y, StringComparison.Ordinal) || y.StartsWith(x, StringComparison.Ordinal)));
    }

    private static bool Intersect<T>(IReadOnlyList<T> a, IReadOnlyList<T> b) => a.Count == 0 || b.Count == 0 || a.Intersect(b).Any();

    /// <summary>Valores de Configurações → Fiscal que alimentam cada operação: mudou um deles, a regra ganha o aviso (CA 6).</summary>
    public static IReadOnlyCollection<string> SettingKeysOf(TaxOperation operation)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal)
        {
            "regime", "crt", "costTaxes", "cstPisCofins", "pisRate", "cofinsRate", "ibsCbsCst", "ibsCbsClassification", "cbsRate", "ibsRate", "ibsCityRate",
        };
        if (IsService(operation))
        {
            keys.UnionWith(["nfseServiceItem", "nfseIssRate", "nfseIssLocation", "nfseIssWithheld"]);
            return keys;
        }

        keys.UnionWith(["cstIcms", "cstIpi", "icmsInternalRate", "icmsInterstateSouthSoutheastRate", "icmsInterstateOtherRate", "icmsImportedRate", "ipiFromNcm"]);
        if (operation == TaxOperation.OwnProductionSale)
        {
            keys.UnionWith(["cfopSaleInStateContributor", "cfopSaleInStateFinal", "cfopSaleInterstateContributor", "cfopSaleInterstateFinal"]);
        }
        else if (operation == TaxOperation.Resale)
        {
            keys.UnionWith(["cfopResaleInState", "cfopResaleInterstate"]);
        }

        return keys;
    }

    /// <summary>A regra é afetada pela mudança (CA 6)? Troca de regime afeta todas.</summary>
    public static bool AffectedBy(TaxRule rule, IReadOnlyCollection<string> changedKeys, bool regimeChanged)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(changedKeys);
        return regimeChanged || SettingKeysOf(rule.Operation).Intersect(changedKeys, StringComparer.Ordinal).Any();
    }

    private static void Cfop(Dictionary<string, string[]> errors, string key, string? value, char first)
    {
        if (value is null || value.Length != 4 || !value.All(char.IsAsciiDigit) || value[0] != first)
        {
            errors.TryAdd(key, [first == '5' ? "Use um CFOP de 4 dígitos começando por 5 (dentro do estado)." : "Use um CFOP de 4 dígitos começando por 6 (fora do estado)."]);
        }
    }

    private static void PisCofins(Dictionary<string, string[]> errors, string key, PisCofinsParameters? value, string label)
    {
        if (value is null)
        {
            errors.TryAdd(key, [$"Informe o {label} da operação."]);
            return;
        }

        if (!FiscalSettingsRules.PisCofinsCst.Contains(value.Cst, StringComparer.Ordinal))
        {
            errors.TryAdd(key + ".cst", [$"Use um CST do {label} da tabela (ex.: 01)."]);
        }

        Rate(errors, key + ".rate", value.Rate);
    }

    private static void Rate(Dictionary<string, string[]> errors, string key, decimal value)
    {
        if (value is < 0 or > FiscalSettingsRules.MaxRate || decimal.Round(value, 4) != value)
        {
            errors.TryAdd(key, ["A alíquota vai de 0% a 100%, com até 4 casas."]);
        }
    }

    [GeneratedRegex("^[0-9]{3}$")]
    private static partial Regex Framework();

    [GeneratedRegex("^[0-9]{1,2}\\.[0-9]{2}$")]
    private static partial Regex ServiceItem();

    [GeneratedRegex("^[0-9]{3}$")]
    private static partial Regex IbsCbsCst();

    [GeneratedRegex("^[0-9]{6}$")]
    private static partial Regex IbsCbsClass();

    [GeneratedRegex("^[0-9]{2,8}$")]
    private static partial Regex NcmPrefix();
}

/// <summary>
/// Seed <c>fiscal-tax-rules</c> (CA 1, ADR 0042): as regras montadas a partir dos valores de Configurações → Fiscal (FIS-02a), nunca de
/// valor inventado fora deles; todas em rascunho e "A confirmar com a contadora". Remessa e retorno de obra não entram (D4).
/// </summary>
public static class TaxRuleSeeds
{
    public const string OwnSaleContributor = "own-sale-contributor";
    public const string OwnSaleNonContributor = "own-sale-non-contributor";
    public const string Resale = "resale";
    public const string Service = "service-installation";

    public static IReadOnlyList<TaxRule> Build(FiscalSettings s, DateOnly validFrom, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(s);
        var crt = FiscalSettings.CrtOf(s.Regime);
        var icms = new IcmsParameters(s.CstIcms, 3, s.IcmsInternalRate, s.IcmsInterstateSouthSoutheastRate, s.IcmsInterstateOtherRate, s.IcmsImportedRate, 0m);
        var ipi = new IpiParameters(s.CstIpi, "999", s.IpiFromNcm, 0m);
        var pis = new PisCofinsParameters(s.CstPisCofins, s.PisRate);
        var cofins = new PisCofinsParameters(s.CstPisCofins, s.CofinsRate);
        var ibsCbs = new IbsCbsParameters(s.IbsCbsCst, s.IbsCbsClassification, s.CbsRate, s.IbsRate, s.IbsCityRate);

        TaxRule Rule(string key, TaxOperation operation, string name, string nature, string? cfopIn, string? cfopOut, TaxRuleConditions conditions)
        {
            var rule = TaxRule.New(null, at, seed: true, seedKey: key);
            rule.Operation = operation;
            rule.Name = name;
            rule.NatureOfOperation = nature;
            rule.ValidFrom = validFrom;
            rule.CfopInternal = cfopIn;
            rule.CfopInterstate = cfopOut;
            rule.CrtScope = crt;
            rule.Pis = pis;
            rule.Cofins = cofins;
            rule.IbsCbs = ibsCbs;
            rule.CostTaxes = [.. s.CostTaxes];
            rule.Conditions = conditions;
            if (!TaxRuleRules.IsService(operation))
            {
                rule.Icms = icms;
                rule.Ipi = ipi;
            }

            return rule;
        }

        var contributor = Rule(OwnSaleContributor, TaxOperation.OwnProductionSale, "Venda de produção própria a contribuinte", "Venda de produção do estabelecimento",
            s.CfopSaleInStateContributor, s.CfopSaleInterstateContributor, new([], [RecipientType.Contributor], []));
        var final = Rule(OwnSaleNonContributor, TaxOperation.OwnProductionSale, "Venda de produção própria a não contribuinte", "Venda de produção do estabelecimento",
            s.CfopSaleInStateFinal, s.CfopSaleInterstateFinal, new([], [RecipientType.Exempt, RecipientType.NonContributor], []));
        final.Difal = new DifalParameters(s.IcmsInternalRate);
        var resale = Rule(Resale, TaxOperation.Resale, "Revenda de peça", "Venda de mercadoria adquirida de terceiros",
            s.CfopResaleInState, s.CfopResaleInterstate, TaxRuleConditions.Any);
        resale.Difal = new DifalParameters(s.IcmsInternalRate);
        var service = Rule(Service, TaxOperation.ServiceProvision, "Prestação de serviço de instalação", "Prestação de serviço", null, null, TaxRuleConditions.Any);
        service.Iss = new IssParameters(s.NfseServiceItem, s.NfseIssRate, s.NfseIssLocation, s.NfseIssWithheld);
        return [contributor, final, resale, service];
    }
}
