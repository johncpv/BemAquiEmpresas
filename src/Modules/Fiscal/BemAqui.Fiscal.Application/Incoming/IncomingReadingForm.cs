using System.Globalization;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>Item conferido e validado (CA 7).</summary>
public sealed record ReadingFormItem(string Code, string Description, string? Ncm, string Unit, decimal Quantity, decimal UnitPrice, decimal Amount);

/// <summary>A nota conferida, pronta para virar <see cref="IncomingInvoice"/>, e os valores canônicos de cada campo (para saber o que foi corrigido).</summary>
public sealed record ReadingForm(
    AccessKeyParts Key, DateTimeOffset IssuedAt, string IssuerName, IncomingInvoiceTotals Totals, IReadOnlyList<ReadingFormItem> Items, IReadOnlyDictionary<string, string?> Values);

/// <summary>
/// Conferência do formulário da leitura assistida e da nota digitada (FIS-05 CA 4, 7, 8 e 9): todo campo marcado como conferido, a chave,
/// a emissão, o emitente, o destinatário, os totais, os itens e a soma; e a comparação com o XML que chega depois (CA 10).
/// </summary>
public static class IncomingReadingForm
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly (string Key, string Label, Func<IncomingInvoiceTotals, decimal> Get)[] TotalFields =
    [
        ("totals.products", "Valor total dos produtos", t => t.Products),
        ("totals.freight", "Valor do frete", t => t.Freight),
        ("totals.insurance", "Valor do seguro", t => t.Insurance),
        ("totals.discount", "Desconto", t => t.Discount),
        ("totals.otherExpenses", "Outras despesas", t => t.OtherExpenses),
        ("totals.ipi", "Valor do IPI", t => t.Ipi),
        ("totals.icmsBase", "Base de cálculo do ICMS", t => t.IcmsBase),
        ("totals.icms", "Valor do ICMS", t => t.Icms),
        ("totals.icmsStBase", "Base de cálculo do ICMS-ST", t => t.IcmsStBase),
        ("totals.icmsSt", "Valor do ICMS-ST", t => t.IcmsSt),
        ("totals.total", "Valor total da nota", t => t.Total),
    ];

    public static Result<ReadingForm> Build(CheckReadingRequest request, string? tenantDocument, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = request.Items ?? [];
        if (items.Count == 0)
        {
            return Invalid("items", "Informe ao menos um item da nota.");
        }

        if (items.Count > IncomingReadingRules.MaxItems)
        {
            return Invalid("items", "A nota pode ter até 990 itens.");
        }

        var confirmed = (request.Confirmed ?? []).ToHashSet(StringComparer.Ordinal);
        var missing = IncomingReadingRules.RequiredKeys(items.Count).Count(k => !confirmed.Contains(k));
        if (missing > 0)
        {
            var message = missing == 1 ? "Falta conferir 1 campo. Marque cada campo como conferido ou corrija o valor." : $"Faltam conferir {missing} campos. Marque cada campo como conferido ou corrija o valor.";
            return Error.Validation("incoming_reading_unchecked", message, new Dictionary<string, string[]> { ["confirmed"] = [message] });
        }

        var key = IncomingReadingRules.Parse(Digits(request.AccessKey));
        if (key is null)
        {
            return Invalid("accessKey", "A chave de acesso precisa ter 44 dígitos e o dígito verificador certo. Confira a chave na DANFE.");
        }

        if (key.Model != "55")
        {
            return Invalid("accessKey", "A chave não é de NF-e (modelo 55).");
        }

        if (IncomingReadingRules.IsPersonIssuer(key))
        {
            return Invalid("accessKey", "Nota de emitente pessoa física não é aceita aqui.");
        }

        if (!BrazilianDocuments.IsValidCnpj(key.IssuerDocument))
        {
            return Invalid("accessKey", "O CNPJ do emitente na chave não é válido. Confira a chave na DANFE.");
        }

        if (request.IssuedOn is not { } issued)
        {
            return Invalid("issuedOn", "Informe a data de emissão.");
        }

        if (issued > today)
        {
            return Invalid("issuedOn", "A data de emissão não pode ser no futuro.");
        }

        if (issued.Year != key.Year || issued.Month != key.Month)
        {
            return Invalid("issuedOn", string.Create(CultureInfo.InvariantCulture, $"A data de emissão não bate com o mês da chave ({key.Month:00}/{key.Year})."));
        }

        var name = OneLine(request.IssuerName);
        if (name is not { Length: >= 2 and <= 200 })
        {
            return Invalid("issuerName", "Informe a razão social do emitente (de 2 a 200 caracteres).");
        }

        var recipient = Digits(request.RecipientDocument);
        if (tenantDocument is null || !string.Equals(recipient, tenantDocument, StringComparison.Ordinal))
        {
            return Invalid("recipientDocument", IncomingInvoiceRules.NotRecipientMessage);
        }

        if (request.Totals is not { } t)
        {
            return Invalid("totals.products", "Informe os totais da nota.");
        }

        var amounts = new decimal[TotalFields.Length];
        decimal?[] raw = [t.Products, t.Freight, t.Insurance, t.Discount, t.OtherExpenses, t.Ipi, t.IcmsBase, t.Icms, t.IcmsStBase, t.IcmsSt, t.Total];
        for (var i = 0; i < TotalFields.Length; i++)
        {
            var (field, label, _) = TotalFields[i];
            var required = field is "totals.products" or "totals.total";
            if (raw[i] is not { } value)
            {
                if (required)
                {
                    return Invalid(field, field == "totals.products" ? "Informe o valor total dos produtos." : "Informe o valor total da nota.");
                }

                continue;
            }

            if (!IsMoney(value))
            {
                return Invalid(field, $"{label}: informe um valor de zero para cima, com até 2 casas.");
            }

            amounts[i] = value;
        }

        var totals = new IncomingInvoiceTotals(amounts[0], amounts[1], amounts[2], amounts[3], amounts[4], amounts[5], amounts[6], amounts[7], amounts[8], amounts[9], amounts[10]);
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["accessKey"] = key.Key, ["issuedAt"] = issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["issuerName"] = name, ["recipientDocument"] = recipient,
        };
        for (var i = 0; i < TotalFields.Length; i++)
        {
            values[TotalFields[i].Key] = Canonical(amounts[i]);
        }

        var lines = new List<ReadingFormItem>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var n = i + 1;
            string K(string field) => IncomingReadingRules.ItemKey(i, field);
            var description = OneLine(item.Description);
            if (description is not { Length: >= 1 and <= 120 })
            {
                return Invalid(K("description"), $"Item {n}: informe a descrição (até 120 caracteres).");
            }

            var code = OneLine(item.Code) ?? string.Empty;
            if (code.Length > 60)
            {
                return Invalid(K("code"), $"Item {n}: o código pode ter até 60 caracteres.");
            }

            var ncm = Digits(item.Ncm);
            if (ncm.Length is not (0 or 8))
            {
                return Invalid(K("ncm"), $"Item {n}: o NCM tem 8 dígitos.");
            }

            var unit = OneLine(item.Unit);
            if (unit is not { Length: >= 1 and <= 6 })
            {
                return Invalid(K("unit"), $"Item {n}: informe a unidade (até 6 caracteres).");
            }

            if (item.Quantity is not { } quantity || quantity <= 0 || decimal.Round(quantity, 4) != quantity || quantity >= 1_000_000_000_000m)
            {
                return Invalid(K("quantity"), $"Item {n}: informe a quantidade maior que zero, com até 4 casas.");
            }

            if (item.UnitPrice is not { } price || price < 0 || decimal.Round(price, 10) != price || price >= 1_000_000_000_000m)
            {
                return Invalid(K("unitPrice"), $"Item {n}: informe o valor unitário de zero para cima, com até 10 casas.");
            }

            if (item.Amount is not { } amount || !IsMoney(amount))
            {
                return Invalid(K("amount"), $"Item {n}: informe o valor total do item, com até 2 casas.");
            }

            lines.Add(new ReadingFormItem(code, description, ncm.Length == 0 ? null : ncm, unit, quantity, price, amount));
            values[K("code")] = code;
            values[K("description")] = description;
            values[K("ncm")] = ncm;
            values[K("unit")] = unit;
            values[K("quantity")] = Canonical(quantity);
            values[K("unitPrice")] = Canonical(price);
            values[K("amount")] = Canonical(amount);
        }

        if (IncomingReadingRules.SumMismatch(lines.Select(l => l.Amount), totals.Products) is { } mismatch)
        {
            return Error.Validation("incoming_reading_sum", mismatch, new Dictionary<string, string[]> { ["totals.products"] = [mismatch] });
        }

        var issuedAt = BusinessTime.StartOfDayUtc(issued).AddHours(12);
        return new ReadingForm(key, issuedAt, name, totals, lines, values);
    }

    /// <summary>
    /// Campos que o usuário corrigiu (CA 13): o valor conferido diferente do lido. Campo que a leitura não trouxe (a nota digitada, o item
    /// que faltou) conta como digitado, e também entra.
    /// </summary>
    public static IReadOnlyList<string> Corrected(IReadOnlyList<ReadingField> read, IReadOnlyDictionary<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(values);
        var lookup = read.GroupBy(f => f.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);
        return values.Where(v => !lookup.TryGetValue(v.Key, out var before) || !string.Equals(Normalize(v.Key, before), v.Value ?? string.Empty, StringComparison.Ordinal))
            .Select(v => v.Key).ToList();
    }

    /// <summary>
    /// Diferenças entre a nota conferida e o XML (CA 10), em texto: emissão, cada total, quantidade de itens e, item a item, código, NCM,
    /// quantidade, valor unitário e total.
    /// </summary>
    public static IReadOnlyList<IncomingXmlDifference> Differences(IncomingInvoice invoice, IReadOnlyList<IncomingInvoiceItem> lines, ParsedNfe nfe)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(nfe);
        var result = new List<IncomingXmlDifference>();
        var checkedOn = DateOnly.FromDateTime(BusinessTime.ToBusiness(invoice.IssuedAt).DateTime);
        var xmlOn = DateOnly.FromDateTime(BusinessTime.ToBusiness(nfe.IssuedAt).DateTime);
        if (checkedOn != xmlOn)
        {
            result.Add(new IncomingXmlDifference("Data de emissão", checkedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), xmlOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)));
        }

        foreach (var (_, label, get) in TotalFields)
        {
            if (get(invoice.Totals) != get(nfe.Totals))
            {
                result.Add(new IncomingXmlDifference(label, IncomingReadingRules.Money(get(invoice.Totals)), IncomingReadingRules.Money(get(nfe.Totals))));
            }
        }

        if (lines.Count != nfe.Items.Count)
        {
            result.Add(new IncomingXmlDifference("Quantidade de itens", lines.Count.ToString(CultureInfo.InvariantCulture), nfe.Items.Count.ToString(CultureInfo.InvariantCulture)));
        }

        foreach (var line in lines)
        {
            if (nfe.Items.FirstOrDefault(i => i.Sequence == line.Sequence) is not { } xml)
            {
                continue;
            }

            var prefix = string.Create(CultureInfo.InvariantCulture, $"Item {line.Sequence}: ");
            if (!string.Equals(line.SupplierCode, xml.SupplierCode, StringComparison.Ordinal))
            {
                result.Add(new IncomingXmlDifference(prefix + "código", line.SupplierCode, xml.SupplierCode));
            }

            if (!string.Equals(line.Ncm ?? string.Empty, xml.Ncm ?? string.Empty, StringComparison.Ordinal))
            {
                result.Add(new IncomingXmlDifference(prefix + "NCM", line.Ncm ?? "—", xml.Ncm ?? "—"));
            }

            if (line.Quantity != xml.Quantity)
            {
                result.Add(new IncomingXmlDifference(prefix + "quantidade", Number(line.Quantity), Number(xml.Quantity)));
            }

            if (line.UnitPrice != xml.UnitPrice)
            {
                result.Add(new IncomingXmlDifference(prefix + "valor unitário", Number(line.UnitPrice), Number(xml.UnitPrice)));
            }

            if (line.Amount != xml.Amount)
            {
                result.Add(new IncomingXmlDifference(prefix + "valor total", IncomingReadingRules.Money(line.Amount), IncomingReadingRules.Money(xml.Amount)));
            }
        }

        return result;
    }

    /// <summary>Número sem os zeros à direita, com ponto (o valor canônico para comparar o lido com o conferido).</summary>
    public static string Canonical(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static string Normalize(string key, string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (key.StartsWith("totals.", StringComparison.Ordinal) || key.EndsWith(".quantity", StringComparison.Ordinal) || key.EndsWith(".unitPrice", StringComparison.Ordinal)
            || key.EndsWith(".amount", StringComparison.Ordinal))
        {
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? Canonical(d) : value;
        }

        return key is "accessKey" or "recipientDocument" || key.EndsWith(".ncm", StringComparison.Ordinal) ? Digits(value) : OneLine(value) ?? string.Empty;
    }

    private static string Number(decimal value) => value.ToString("#,##0.##########", PtBr);

    private static bool IsMoney(decimal value) => value >= 0 && decimal.Round(value, 2) == value && value < 1_000_000_000_000m;

    internal static string Digits(string? value) => new((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    /// <summary>Texto numa linha só, sem caractere de controle e sem espaço repetido.</summary>
    internal static string? OneLine(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = string.Join(' ', new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return text.Length == 0 ? null : text;
    }

    private static Error Invalid(string field, string message) => Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>
/// Conferência da resposta do worker (FIS-05 CA 4): é dado de fora (o worker leu um arquivo do fornecedor). Só as chaves do formulário,
/// cada valor no formato do campo e com o tamanho limitado; a chave com DV errado é descartada com aviso; o CNPJ do texto que contradiz a
/// chave é ignorado; o destinatário diferente do tenant fica incerto.
/// </summary>
public static class IncomingReadingSanitizer
{
    public static (IReadOnlyList<ReadingField> Fields, string? Key, IReadOnlyList<string> Warnings) Sanitize(PdfReaderReply reply, string? tenantDocument)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Outcome != PdfReadingOutcome.Read)
        {
            return ([], null, []);
        }

        var warnings = new List<string>();
        var byKey = new Dictionary<string, ReadingField>(StringComparer.Ordinal);
        string? issuerText = null;
        foreach (var field in reply.Fields.Take(IncomingReadingRules.HeaderKeys.Count + 1 + (IncomingReadingRules.MaxItems * IncomingReadingRules.ItemFieldKeys.Count)))
        {
            if (field is null || !IncomingReadingRules.IsKnownKey(field.Key) || byKey.ContainsKey(field.Key) || (field.Key == "issuerDocument" && issuerText is not null))
            {
                continue;
            }

            var value = Clean(field.Key, field.Value);
            if (field.Key == "issuerDocument")
            {
                issuerText = value;
                continue;
            }

            var state = value is null ? ReadingFieldState.NotFound : field.State == ReadingFieldState.NotFound ? ReadingFieldState.Uncertain : field.State;
            byKey[field.Key] = new ReadingField(field.Key, value, state);
        }

        string? key = null;
        if (byKey.GetValueOrDefault("accessKey") is { Value: { } candidate })
        {
            if (IncomingReadingRules.Parse(candidate) is { } parts)
            {
                key = parts.Key;
                if (issuerText is not null && !string.Equals(issuerText, parts.IssuerDocument, StringComparison.Ordinal))
                {
                    warnings.Add(IncomingReadingRules.IssuerMismatchWarning);
                }
            }
            else
            {
                byKey["accessKey"] = new ReadingField("accessKey", null, ReadingFieldState.NotFound);
                warnings.Add(IncomingReadingRules.KeyDiscardedWarning);
            }
        }

        if (byKey.GetValueOrDefault("recipientDocument") is { Value: { } recipient } && !string.Equals(recipient, tenantDocument, StringComparison.Ordinal))
        {
            byKey["recipientDocument"] = new ReadingField("recipientDocument", recipient, ReadingFieldState.Uncertain);
        }

        var fields = IncomingReadingRules.HeaderKeys.Select(k => byKey.GetValueOrDefault(k) ?? new ReadingField(k, null, ReadingFieldState.NotFound)).ToList();
        for (var i = 0; i < IncomingReadingRules.MaxItems; i++)
        {
            var keys = IncomingReadingRules.ItemFieldKeys.Select(f => IncomingReadingRules.ItemKey(i, f)).ToList();
            if (!keys.Any(byKey.ContainsKey))
            {
                break;
            }

            fields.AddRange(keys.Select(k => byKey.GetValueOrDefault(k) ?? new ReadingField(k, null, ReadingFieldState.NotFound)));
        }

        return (fields, key, warnings);
    }

    private static string? Clean(string key, string? value)
    {
        if (value is null)
        {
            return null;
        }

        if (key is "accessKey")
        {
            var digits = IncomingReadingForm.Digits(value);
            return digits.Length == 44 ? digits : null;
        }

        if (key is "recipientDocument" or "issuerDocument")
        {
            var digits = IncomingReadingForm.Digits(value);
            return digits.Length == 14 ? digits : null;
        }

        if (key is "issuedAt")
        {
            return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        }

        if (key.StartsWith("totals.", StringComparison.Ordinal) || key.EndsWith(".quantity", StringComparison.Ordinal) || key.EndsWith(".unitPrice", StringComparison.Ordinal)
            || key.EndsWith(".amount", StringComparison.Ordinal))
        {
            return value.Length <= 30 && decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) && d < 1_000_000_000_000m
                ? IncomingReadingForm.Canonical(d)
                : null;
        }

        if (key.EndsWith(".ncm", StringComparison.Ordinal))
        {
            var digits = IncomingReadingForm.Digits(value);
            return digits.Length is > 0 and <= 8 ? digits : null;
        }

        var max = key == "issuerName" ? 200 : key.EndsWith(".description", StringComparison.Ordinal) ? 120 : key.EndsWith(".code", StringComparison.Ordinal) ? 60 : 6;
        var text = IncomingReadingForm.OneLine(value);
        return text is null ? null : text.Length > max ? text[..max] : text;
    }
}
