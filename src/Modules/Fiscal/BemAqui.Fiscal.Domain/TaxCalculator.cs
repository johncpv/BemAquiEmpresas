using System.Globalization;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Domain;

/// <summary>Códigos dos tributos calculados (as linhas de <see cref="TaxItemResult.Taxes"/> e dos totais).</summary>
public static class TaxCodes
{
    public const string Icms = "ICMS";
    public const string IcmsSt = "ICMS_ST";
    public const string Fcp = "FCP";
    public const string Ipi = "IPI";
    public const string Pis = "PIS";
    public const string Cofins = "COFINS";
    public const string Iss = "ISS";
    public const string Cbs = "CBS";
    public const string IbsState = "IBS_UF";
    public const string IbsCity = "IBS_MUN";
    public const string Difal = "DIFAL";

    public static readonly string[] Order = [Icms, IcmsSt, Fcp, Difal, Ipi, Pis, Cofins, Iss, Cbs, IbsState, IbsCity];
}

/// <summary>Quem emite: UF e CRT (FIS-02a).</summary>
public sealed record TaxIssuer(string State, int Crt);

/// <summary>Quem recebe: UF, tipo (contribuinte, isento ou não contribuinte) e se é consumidor final.</summary>
public sealed record TaxRecipient(string State, RecipientType Type, bool FinalConsumer);

/// <summary>
/// Item da nota: NCM, origem (0 a 8), CEST, quantidade, valor unitário, desconto, frete e outras despesas já rateados, e a alíquota do IPI
/// da TIPI para o NCM (quando a regra diz "pela TIPI do NCM"; a tabela TIPI não é importada no MVP).
/// </summary>
public sealed record TaxItem(string? Description, string? Ncm, int Origin, string? Cest, decimal Quantity, decimal UnitPrice, decimal Discount = 0m, decimal Freight = 0m, decimal OtherExpenses = 0m, decimal? IpiRate = null);

/// <summary>Um tributo de um item: CST, base, alíquota e valor (2 casas), se entra no custo do projeto (CA 11) e se soma ao total da nota.</summary>
public sealed record TaxLine(string Code, string? Cst, decimal Base, decimal Rate, decimal Amount, bool InProjectCost, bool InInvoiceTotal);

/// <summary>Resultado de um item (CA 7): a regra (versão) usada, o CFOP, os valores e os tributos.</summary>
public sealed record TaxItemResult(
    int Index, string? Description, string? Ncm, Guid RuleId, Guid RuleVersionId, int RuleVersion, string RuleName, string? Cfop,
    decimal Quantity, decimal UnitPrice, decimal GrossAmount, decimal Discount, decimal Freight, decimal OtherExpenses,
    IReadOnlyList<TaxLine> Taxes, decimal ItemTotal, decimal ProjectCost);

/// <summary>Total de um tributo na nota: soma das bases e dos valores dos itens.</summary>
public sealed record TaxTotal(string Code, decimal Base, decimal Amount);

/// <summary>
/// Nota calculada: itens, totais (soma dos itens, sem novo arredondamento), total da nota, custo do projeto e o que bloqueia a emissão
/// (ex.: DIFAL, CA 8). Nada é gravado.
/// </summary>
public sealed record TaxCalculation(
    IReadOnlyList<TaxItemResult> Items, decimal Products, decimal Discount, decimal Freight, decimal OtherExpenses, IReadOnlyList<TaxTotal> Totals,
    decimal InvoiceTotal, decimal ProjectCost, IReadOnlyList<string> Blocks);

/// <summary>
/// Motor de cálculo dos tributos (FIS-02b CA 7 a 9, puro: sem banco, sem relógio, sem Emissor). Recebe a operação, o emitente, o destinatário,
/// os itens e a regra de cada item (resolvida antes pelo <c>TaxRuleResolver</c>) e devolve por item CFOP, CST, bases, alíquotas e valores de
/// ICMS, ICMS-ST, FCP, DIFAL, IPI, PIS, COFINS, ISS, CBS e IBS. Arredonda só no item (2 casas, <see cref="MidpointRounding.AwayFromZero"/>);
/// o total é a soma dos itens. Fórmulas (casos sintéticos tirados da legislação, a confirmar com a contadora):
/// <list type="bullet">
/// <item>Base da operação = valor dos produtos − desconto + frete + outras despesas.</item>
/// <item>IPI sobre a base da operação, nos CST 50 e 99.</item>
/// <item>ICMS sobre a base (com o IPI quando o destinatário não é contribuinte ou é consumidor final, CF art. 155 §2º XI), com a redução;
/// alíquota interna, interestadual por grupo de UF ou 4% para importado; CST 00, 10, 20, 51, 70 e 90; CSOSN não destaca ICMS próprio.</item>
/// <item>ICMS-ST = (base + IPI) × (1 + MVA) × (1 − redução) × alíquota interna − ICMS próprio, nos CST 10, 30, 70 e 90 e CSOSN 201, 202, 203 e 900.</item>
/// <item>DIFAL (base única) = base do ICMS × (alíquota interna do destino − interestadual), para não contribuinte de outra UF; bloqueia a emissão.</item>
/// <item>PIS e COFINS sobre a base da operação nos CST 01 e 02; ISS sobre serviço − desconto; CBS e IBS sobre a base da operação.</item>
/// </list>
/// </summary>
public static class TaxCalculator
{
    public const string DifalBlockedMessage = "A nota com DIFAL ainda não é suportada pelo Emissor Fiscal. Fale com a contadora para emitir esta venda.";
    public const string InvalidItemCode = "tax_invalid_item";
    public const string IpiRateMissingCode = "tax_ipi_rate_missing";

    private static readonly string[] IcmsTaxedCst = ["00", "10", "20", "51", "70", "90"];
    private static readonly string[] IcmsReducedCst = ["20", "51", "70", "90"];
    private static readonly string[] StCst = ["10", "30", "70", "90", "201", "202", "203", "900"];
    private static readonly string[] IpiTaxedCst = ["50", "99"];
    private static readonly string[] PisCofinsTaxedCst = ["01", "02"];

    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Calcula a nota (CA 7); <paramref name="rules"/> tem a regra de cada item, na mesma ordem.</summary>
    public static Result<TaxCalculation> Calculate(TaxIssuer issuer, TaxRecipient recipient, IReadOnlyList<TaxItem> items, IReadOnlyList<TaxRule> rules)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(rules);
        if (items.Count == 0 || items.Count != rules.Count)
        {
            return Invalid("items", "Informe ao menos um item.");
        }

        if (!TaxRuleRules.States.Contains(issuer.State, StringComparer.Ordinal))
        {
            return Invalid("issuer", "Informe a UF da empresa em Dados da empresa.");
        }

        if (!TaxRuleRules.States.Contains(recipient.State, StringComparer.Ordinal))
        {
            return Invalid("recipientState", "Escolha a UF do destinatário.");
        }

        var results = new List<TaxItemResult>(items.Count);
        var blocks = new List<string>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = CalculateItem(i, issuer, recipient, items[i], rules[i]);
            if (item.IsFailure)
            {
                return item.Error!;
            }

            results.Add(item.Value);
            if (item.Value.Taxes.Any(t => t.Code == TaxCodes.Difal) && !blocks.Contains(DifalBlockedMessage))
            {
                blocks.Add(DifalBlockedMessage);
            }
        }

        var totals = TaxCodes.Order
            .Select(code => (code, lines: results.SelectMany(r => r.Taxes).Where(t => t.Code == code).ToList()))
            .Where(x => x.lines.Count > 0)
            .Select(x => new TaxTotal(x.code, x.lines.Sum(l => l.Base), x.lines.Sum(l => l.Amount)))
            .ToList();
        return new TaxCalculation(results, results.Sum(r => r.GrossAmount), results.Sum(r => r.Discount), results.Sum(r => r.Freight), results.Sum(r => r.OtherExpenses),
            totals, results.Sum(r => r.ItemTotal), results.Sum(r => r.ProjectCost), blocks);
    }

    /// <summary>Um item com a sua regra.</summary>
    public static Result<TaxItemResult> CalculateItem(int index, TaxIssuer issuer, TaxRecipient recipient, TaxItem item, TaxRule rule)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(rule);
        var prefix = $"items[{index.ToString(CultureInfo.InvariantCulture)}].";
        if (item.Quantity <= 0 || item.UnitPrice < 0 || item.Discount < 0 || item.Freight < 0 || item.OtherExpenses < 0)
        {
            return Invalid(prefix + "quantity", "Quantidade maior que zero e valores sem sinal negativo.");
        }

        if (item.Origin is < 0 or > 8)
        {
            return Invalid(prefix + "origin", "A origem da mercadoria vai de 0 a 8.");
        }

        var gross = Round(item.Quantity * item.UnitPrice);
        if (item.Discount > gross)
        {
            return Invalid(prefix + "discount", "O desconto não passa do valor do item.");
        }

        var discount = Round(item.Discount);
        var service = TaxRuleRules.IsService(rule.Operation);
        var freight = service ? 0m : Round(item.Freight);
        var other = service ? 0m : Round(item.OtherExpenses);
        var operationBase = gross - discount + freight + other;
        var cost = new HashSet<string>(rule.CostTaxes, StringComparer.Ordinal);
        var lines = new List<TaxLine>();
        var interstate = !string.Equals(issuer.State, recipient.State, StringComparison.Ordinal);

        // IPI
        var ipiAmount = 0m;
        if (rule.Ipi is { } ipi)
        {
            var taxed = IpiTaxedCst.Contains(ipi.Cst, StringComparer.Ordinal);
            var rate = 0m;
            if (taxed)
            {
                if (ipi.FromNcm && item.IpiRate is null)
                {
                    return new Error(IpiRateMissingCode, $"Informe a alíquota do IPI da TIPI para o NCM {item.Ncm ?? "do item"}.", ErrorType.Validation)
                    {
                        Fields = new Dictionary<string, string[]> { [prefix + "ipiRate"] = ["Informe a alíquota do IPI da TIPI."] },
                    };
                }

                rate = ipi.FromNcm ? item.IpiRate!.Value : ipi.Rate;
            }

            var ipiBase = taxed ? operationBase : 0m;
            ipiAmount = Round(ipiBase * rate / 100m);
            lines.Add(new TaxLine(TaxCodes.Ipi, ipi.Cst, ipiBase, rate, ipiAmount, cost.Contains("IPI"), true));
        }

        // ICMS próprio, FCP, ST e DIFAL
        var icmsAmount = 0m;
        if (rule.Icms is { } icms)
        {
            var rate = !interstate ? icms.InternalRate
                : TaxRuleRules.ImportedOrigins.Contains(item.Origin) ? icms.ImportedRate
                : TaxRuleRules.SouthSoutheastExceptEs.Contains(issuer.State, StringComparer.Ordinal) && !TaxRuleRules.SouthSoutheastExceptEs.Contains(recipient.State, StringComparer.Ordinal)
                    ? icms.InterstateOtherRate
                    : icms.InterstateSouthSoutheastRate;
            var normal = issuer.Crt == 3;
            var taxed = normal && IcmsTaxedCst.Contains(icms.Cst, StringComparer.Ordinal);
            var finalUse = recipient.FinalConsumer || recipient.Type != RecipientType.Contributor;
            var reduction = IcmsReducedCst.Contains(icms.Cst, StringComparer.Ordinal) ? icms.BaseReduction : 0m;
            var icmsBase = taxed ? Round((operationBase + (finalUse ? ipiAmount : 0m)) * (1m - reduction / 100m)) : 0m;
            icmsAmount = taxed ? Round(icmsBase * rate / 100m) : 0m;
            lines.Add(new TaxLine(TaxCodes.Icms, icms.Cst, icmsBase, taxed ? rate : 0m, icmsAmount, cost.Contains("ICMS"), false));

            if (rule.Fcp is { Rate: > 0 } fcp && taxed && !interstate)
            {
                lines.Add(new TaxLine(TaxCodes.Fcp, null, icmsBase, fcp.Rate, Round(icmsBase * fcp.Rate / 100m), cost.Contains("ICMS"), false));
            }

            if (rule.IcmsSt is { } st && StCst.Contains(icms.Cst, StringComparer.Ordinal))
            {
                var stBase = Round((operationBase + ipiAmount) * (1m + st.Mva / 100m) * (1m - st.BaseReduction / 100m));
                var stAmount = Math.Max(0m, Round(stBase * st.InternalRate / 100m) - icmsAmount);
                lines.Add(new TaxLine(TaxCodes.IcmsSt, icms.Cst, stBase, st.InternalRate, stAmount, false, true));
            }

            if (rule.Difal is { } difal && interstate && recipient.Type != RecipientType.Contributor && taxed)
            {
                var difalRate = Math.Max(0m, difal.DestinationInternalRate - rate);
                lines.Add(new TaxLine(TaxCodes.Difal, null, icmsBase, difalRate, Round(icmsBase * difalRate / 100m), false, false));
            }
        }

        // PIS e COFINS
        foreach (var (code, value) in new[] { (TaxCodes.Pis, rule.Pis), (TaxCodes.Cofins, rule.Cofins) })
        {
            if (value is null)
            {
                continue;
            }

            var taxed = PisCofinsTaxedCst.Contains(value.Cst, StringComparer.Ordinal);
            var pcBase = taxed ? operationBase : 0m;
            var rate = taxed ? value.Rate : 0m;
            lines.Add(new TaxLine(code, value.Cst, pcBase, rate, Round(pcBase * rate / 100m), cost.Contains(code), false));
        }

        // ISS (prestação de serviço)
        if (rule.Iss is { } iss && service)
        {
            var issBase = gross - discount;
            lines.Add(new TaxLine(TaxCodes.Iss, null, issBase, iss.Rate, Round(issBase * iss.Rate / 100m), cost.Contains("ISS"), false));
        }

        // IBS e CBS (assumidos obrigatórios quando a regra os tem, ponto 9; em 2026 não somam ao total)
        if (rule.IbsCbs is { } ibs)
        {
            var inCost = cost.Contains("IBS_CBS");
            lines.Add(new TaxLine(TaxCodes.Cbs, ibs.Cst, operationBase, ibs.CbsRate, Round(operationBase * ibs.CbsRate / 100m), inCost, false));
            lines.Add(new TaxLine(TaxCodes.IbsState, ibs.Cst, operationBase, ibs.IbsStateRate, Round(operationBase * ibs.IbsStateRate / 100m), inCost, false));
            lines.Add(new TaxLine(TaxCodes.IbsCity, ibs.Cst, operationBase, ibs.IbsCityRate, Round(operationBase * ibs.IbsCityRate / 100m), inCost, false));
        }

        var cfop = service ? null : interstate ? rule.CfopInterstate : rule.CfopInternal;
        var itemTotal = operationBase + lines.Where(l => l.InInvoiceTotal).Sum(l => l.Amount);
        return new TaxItemResult(index, item.Description, item.Ncm, rule.RuleId, rule.Id, rule.RuleVersion, rule.Name, cfop, item.Quantity, item.UnitPrice, gross, discount, freight, other,
            lines, itemTotal, lines.Where(l => l.InProjectCost).Sum(l => l.Amount));
    }

    private static Error Invalid(string field, string message) => Error.Validation(InvalidItemCode, message, new Dictionary<string, string[]> { [field] = [message] });
}
