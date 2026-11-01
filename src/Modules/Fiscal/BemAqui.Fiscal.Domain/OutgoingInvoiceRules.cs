using System.Globalization;

namespace BemAqui.Fiscal.Domain;

/// <summary>Uma pré-condição da emissão (CA 6): o que falta e o link para resolver.</summary>
public sealed record IssuePrecondition(string Key, bool Ok, string Text, string? Link);

/// <summary>
/// Regras puras da NF-e de saída (FIS-03a CA 1, 3, 4 e 6): o tipo do destinatário pelo cadastro, a operação pelo tipo do item, o rateio
/// do frete, as informações preenchidas (séries e endereço da obra), os limites do esquema e o motivo de cada pré-condição. Sem banco.
/// </summary>
public static class OutgoingInvoiceRules
{
    public const string SerialsPrefix = "Séries: ";
    public const string SitePrefix = "Endereço da obra: ";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Indicador do destinatário (<c>indIEDest</c>): pessoa física é não contribuinte; pessoa jurídica com IE é contribuinte, com
    /// "ISENTO" é isenta e sem IE é não contribuinte (a regra mais conservadora: sem IE, nada de crédito de ICMS para o cliente).
    /// </summary>
    public static RecipientType RecipientTypeOf(bool company, string? stateRegistration)
    {
        var ie = (stateRegistration ?? string.Empty).Trim();
        if (!company || ie.Length == 0)
        {
            return RecipientType.NonContributor;
        }

        return ie.Equals("ISENTO", StringComparison.OrdinalIgnoreCase) ? RecipientType.Exempt : RecipientType.Contributor;
    }

    /// <summary>Equipamento sempre é produção própria; peça é produção própria se fabricada (produto acabado ou componente), senão revenda (D4).</summary>
    public static TaxOperation OperationFor(string quoteItemKind, bool fabricated) =>
        quoteItemKind == "Equipment" || fabricated ? TaxOperation.OwnProductionSale : TaxOperation.Resale;

    /// <summary>Rateio pelo valor de cada item, em centavos: a soma bate com o total (a sobra vai para o último item com valor).</summary>
    public static IReadOnlyList<decimal> Apportion(IReadOnlyList<decimal> bases, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(bases);
        var result = new decimal[bases.Count];
        var total = bases.Sum();
        if (amount <= 0 || bases.Count == 0)
        {
            return result;
        }

        var last = -1;
        for (var i = 0; i < bases.Count; i++)
        {
            result[i] = total > 0 ? TaxCalculator.Round(amount * bases[i] / total) : (i == 0 ? amount : 0m);
            if (bases[i] > 0 || total == 0)
            {
                last = i;
            }
        }

        var rest = amount - result.Sum();
        result[last < 0 ? 0 : last] += rest;
        return result;
    }

    /// <summary>As séries nas informações do item (CA 1).</summary>
    public static string? SerialsInfo(IReadOnlyCollection<string> serials) => serials.Count == 0 ? null : SerialsPrefix + string.Join(", ", serials);

    /// <summary>O endereço da obra nas informações complementares (D4: só a venda, sem remessa).</summary>
    public static string? SiteInfo(string? siteAddress) => NfeTextRules.Clean(siteAddress) is { } clean ? SitePrefix + clean : null;

    /// <summary>
    /// Informações complementares que vão ao Emissor: as da nota e, enquanto o contrato do Emissor não tiver o grupo de transporte e o
    /// <c>infAdProd</c> (lacuna L-d), as informações de cada item e o transporte do romaneio (volumes e peso).
    /// </summary>
    public static string? ComplementaryForEmitter(string? additional, IEnumerable<(int Sequence, string? Info)> items, int? volumes, decimal? grossWeight, string? species, string? carrierName)
    {
        ArgumentNullException.ThrowIfNull(items);
        var parts = new List<string>();
        if (NfeTextRules.Clean(additional) is { } own)
        {
            parts.Add(own);
        }

        parts.AddRange(items.Where(i => NfeTextRules.Clean(i.Info) is not null).Select(i => $"Item {i.Sequence.ToString(CultureInfo.InvariantCulture)}: {NfeTextRules.Clean(i.Info)}"));
        var transport = new List<string>();
        if (NfeTextRules.Clean(carrierName) is { } carrier)
        {
            transport.Add("transportadora " + carrier);
        }

        if (volumes is { } v)
        {
            transport.Add($"{v.ToString(CultureInfo.InvariantCulture)} {(NfeTextRules.Clean(species) ?? "volumes").ToLowerInvariant()}");
        }

        if (grossWeight is { } w)
        {
            transport.Add("peso bruto " + w.ToString("0.###", PtBr) + " kg");
        }

        if (transport.Count > 0)
        {
            parts.Add("Transporte: " + string.Join(", ", transport) + ".");
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    /// <summary>
    /// Limites do esquema da NF-e 4.00 (CA 4), depois do saneamento: campo da API → mensagem ("A descrição do item 3 passa de 120
    /// caracteres."). Vazio, o texto cabe.
    /// </summary>
    public static Dictionary<string, string[]> TextErrors(OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, string? emitterComplementary)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(items);
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Check(string field, string? value, int max, string message)
        {
            if (!NfeTextRules.Fits(value, max))
            {
                errors[field] = [message];
            }
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var n = item.Sequence.ToString(CultureInfo.InvariantCulture);
            Check($"items[{i.ToString(CultureInfo.InvariantCulture)}].description", item.Description, NfeTextRules.ProductDescription, $"A descrição do item {n} passa de {NfeTextRules.ProductDescription} caracteres.");
            if (string.IsNullOrEmpty(item.Description))
            {
                errors[$"items[{i.ToString(CultureInfo.InvariantCulture)}].description"] = [$"Informe a descrição do item {n}."];
            }

            Check($"items[{i.ToString(CultureInfo.InvariantCulture)}].additionalInfo", item.AdditionalInfo, NfeTextRules.ItemInformation, $"As informações do item {n} passam de {NfeTextRules.ItemInformation} caracteres.");
        }

        Check("additionalInformation", invoice.AdditionalInformation, NfeTextRules.ComplementaryInformation, $"As informações complementares passam de {NfeTextRules.ComplementaryInformation} caracteres.");
        if (!errors.ContainsKey("additionalInformation"))
        {
            Check("additionalInformation", emitterComplementary, NfeTextRules.ComplementaryInformation,
                $"As informações complementares, com as dos itens e o transporte, passam de {NfeTextRules.ComplementaryInformation} caracteres.");
        }

        Check("recipient.name", invoice.RecipientName, NfeTextRules.Name, $"O nome do destinatário passa de {NfeTextRules.Name} caracteres. Use a razão social abreviada no cadastro do cliente.");
        Check("recipient.street", invoice.RecipientStreet, NfeTextRules.Street, $"O logradouro do destinatário passa de {NfeTextRules.Street} caracteres.");
        Check("recipient.number", invoice.RecipientNumber, NfeTextRules.Number, $"O número do endereço do destinatário passa de {NfeTextRules.Number} caracteres.");
        Check("recipient.complement", invoice.RecipientComplement, NfeTextRules.Complement, $"O complemento do endereço do destinatário passa de {NfeTextRules.Complement} caracteres.");
        Check("recipient.district", invoice.RecipientDistrict, NfeTextRules.District, $"O bairro do destinatário passa de {NfeTextRules.District} caracteres.");
        Check("recipient.city", invoice.RecipientCityName, NfeTextRules.City, $"O município do destinatário passa de {NfeTextRules.City} caracteres.");
        return errors;
    }

    /// <summary>Pré-condições que a nota mesma responde (CA 6): destinatário, IBGE, itens com regra, cálculo fechado, valor e sem DIFAL.</summary>
    public static IReadOnlyList<IssuePrecondition> InvoicePreconditions(OutgoingInvoice invoice, IReadOnlyList<OutgoingInvoiceItem> items, string partyLink)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(items);
        var digits = new string((invoice.RecipientDocument ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        var list = new List<IssuePrecondition>
        {
            new("recipientDocument", digits.Length is 11 or 14, "Informe o CPF ou o CNPJ do cliente no cadastro.", partyLink),
            new("recipientAddress", invoice.RecipientCityCode is { Length: 7 } code && code.All(char.IsAsciiDigit) && !string.IsNullOrEmpty(invoice.RecipientStreet) && !string.IsNullOrEmpty(invoice.RecipientState),
                "Informe o endereço do cliente com o código IBGE do município.", partyLink),
            new("items", items.Count > 0, "A nota precisa de ao menos um item.", null),
            new("calculation", invoice.CalculationError is null, invoice.CalculationError ?? "Os tributos estão calculados.", null),
            new("rules", items.Count > 0 && items.All(i => i.TaxRuleVersionId is not null), "Cada item precisa de uma regra fiscal ativa.", "/app/fiscal/regras"),
            new("values", items.All(i => i.UnitPrice > 0 && i.Quantity > 0), "Informe a quantidade e o valor de cada item.", null),
            new("difal", items.All(i => i.Taxes.All(t => t.Code != TaxCodes.Difal)), TaxCalculator.DifalBlockedMessage, null),
        };
        return list;
    }
}
