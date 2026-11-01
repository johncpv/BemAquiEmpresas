using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BemAqui.PdfReader;

/// <summary>Palavra do PDF com a posição na página (coordenadas do PDF: y cresce para cima).</summary>
internal sealed record PdfWord(string Text, double Left, double Right, double Bottom, double Top, int Page)
{
    public double Center => (Left + Right) / 2;
}

/// <summary>
/// Leitura dos campos da DANFE a partir das palavras e das posições (FIS-05 CA 4 e 7), sem OCR e sem executar nada: o rótulo de cada
/// quadro ("VALOR TOTAL DA NOTA") e o valor logo abaixo; a chave de 44 dígitos; a tabela dos itens pelas colunas do cabeçalho. Tudo é
/// sugestão: o que não bate (soma dos itens, quantidade × preço) sai como "incerto" e a conferência humana decide.
/// </summary>
internal static partial class DanfeParser
{
    public const string Read = "Read";
    public const string Uncertain = "Uncertain";
    public const string NotFound = "NotFound";

    public const int MaxItems = 990;

    private static readonly (string Key, string[] Labels, string[] NotFollowedBy)[] TotalLabels =
    [
        ("totals.icmsStBase", ["BASE DE CALCULO DO ICMS ST", "BASE DE CALCULO DO ICMS SUBST", "BASE DE CALC. ICMS S.T.", "BASE DE CALC. ICMS ST", "BASE DE CALC. DO ICMS ST", "BASE CALC. ICMS ST", "BASE CALC. ICMS S.T.", "BC ICMS ST"], []),
        ("totals.icmsSt", ["VALOR DO ICMS SUBSTITUICAO", "VALOR DO ICMS SUBST", "VALOR DO ICMS ST", "VALOR DO ICMS S.T.", "V. ICMS SUBST", "V. ICMS ST", "VALOR ICMS ST"], []),
        ("totals.icmsBase", ["BASE DE CALCULO DO ICMS", "BASE DE CALC. DO ICMS", "BASE DE CALCULO ICMS", "BASE CALC. ICMS", "BASE DE CALC. ICMS", "BC ICMS"], ["ST", "S.T.", "SUBST", "SUBSTITUICAO"]),
        ("totals.icms", ["VALOR DO ICMS", "V. ICMS", "VALOR ICMS"], ["ST", "S.T.", "SUBST", "SUBSTITUICAO", "DESONERADO", "UF"]),
        ("totals.products", ["VALOR TOTAL DOS PRODUTOS", "V. TOTAL PRODUTOS", "V. TOTAL DOS PRODUTOS", "VALOR DOS PRODUTOS", "VALOR TOTAL PRODUTOS"], []),
        ("totals.freight", ["VALOR DO FRETE", "V. FRETE", "VALOR FRETE"], []),
        ("totals.insurance", ["VALOR DO SEGURO", "V. SEGURO", "VALOR SEGURO"], []),
        ("totals.discount", ["DESCONTO", "V. DESCONTO", "VALOR DO DESCONTO"], []),
        ("totals.otherExpenses", ["OUTRAS DESPESAS ACESSORIAS", "OUTRAS DESPESAS", "OUTRAS DESP."], []),
        ("totals.ipi", ["VALOR TOTAL DO IPI", "VALOR DO IPI", "V. TOTAL IPI", "V. IPI", "VALOR IPI"], []),
        ("totals.total", ["VALOR TOTAL DA NOTA", "V. TOTAL DA NOTA", "VALOR TOTAL DA NF", "V. TOTAL DA NF"], []),
    ];

    private static readonly string[] StopTable = ["DADOS ADICIONAIS", "CALCULO DO ISSQN", "INFORMACOES COMPLEMENTARES", "RESERVADO AO FISCO"];

    /// <summary>Campos lidos das palavras do PDF (as páginas na ordem); nunca o texto.</summary>
    public static IReadOnlyList<ReadField> Parse(IReadOnlyList<PdfWord> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var lines = Lines(words);
        var fields = new List<ReadField>();
        var key = AccessKey(lines);
        fields.Add(new ReadField("accessKey", key.Value, key.State));

        var recipientAt = lines.FindIndex(l => l.Norm.Contains("DESTINATARIO", StringComparison.Ordinal));
        var issuedAt = LabelValue(lines, ["DATA DA EMISSAO", "DATA DE EMISSAO", "DATA EMISSAO"], [], 0, lines.Count, ParseDate);
        fields.Add(issuedAt is { } d ? new ReadField("issuedAt", d, Read) : new ReadField("issuedAt", null, NotFound));

        var name = IssuerName(lines);
        fields.Add(new ReadField("issuerName", name, name is null ? NotFound : Uncertain));
        var issuer = IssuerDocument(lines, recipientAt < 0 ? lines.Count : recipientAt);
        if (issuer is not null)
        {
            fields.Add(new ReadField("issuerDocument", issuer, Read));
        }

        fields.Add(Recipient(lines, recipientAt));

        var totalsFrom = lines.FindIndex(l => l.Norm.Contains("CALCULO DO IMPOSTO", StringComparison.Ordinal));
        var totalsTo = totalsFrom < 0 ? lines.Count : lines.FindIndex(totalsFrom, l => l.Norm.Contains("TRANSPORTADOR", StringComparison.Ordinal) || l.Norm.Contains("DADOS DO PRODUTO", StringComparison.Ordinal) || l.Norm.Contains("DADOS DOS PRODUTOS", StringComparison.Ordinal));
        var totals = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        foreach (var (field, labels, notFollowed) in TotalLabels)
        {
            var value = LabelValue(lines, labels, notFollowed, Math.Max(0, totalsFrom), totalsTo < 0 ? lines.Count : totalsTo, ParseMoney);
            totals[field] = value is null ? null : decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
        }

        var items = Items(lines);
        var sum = items.Sum(i => i.Amount ?? 0m);
        var products = totals["totals.products"];
        var expectedTotal = (products ?? 0) - (totals["totals.discount"] ?? 0) + (totals["totals.freight"] ?? 0) + (totals["totals.insurance"] ?? 0)
            + (totals["totals.otherExpenses"] ?? 0) + (totals["totals.ipi"] ?? 0) + (totals["totals.icmsSt"] ?? 0);
        foreach (var (field, _, _) in TotalLabels.OrderBy(t => Order(t.Key)))
        {
            var value = totals[field];
            var state = value is null ? NotFound
                : field == "totals.products" && (items.Count == 0 || sum != value) ? Uncertain
                : field == "totals.total" && expectedTotal != value ? Uncertain
                : Read;
            fields.Add(new ReadField(field, value?.ToString(CultureInfo.InvariantCulture), state));
        }

        for (var i = 0; i < items.Count; i++)
        {
            fields.AddRange(items[i].Fields(i));
        }

        return fields;
    }

    private static int Order(string key) => Array.IndexOf(
        ["totals.products", "totals.freight", "totals.insurance", "totals.discount", "totals.otherExpenses", "totals.ipi", "totals.icmsBase", "totals.icms", "totals.icmsStBase", "totals.icmsSt", "totals.total"], key);

    /// <summary>Texto em maiúsculas sem acento, para achar os rótulos.</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            builder.Append(Fold(c));
        }

        return builder.ToString();
    }

    /// <summary>Maiúscula sem acento (tabela própria: o worker roda em modo invariante, sem ICU).</summary>
    private static char Fold(char c) => c switch
    {
        'á' or 'à' or 'â' or 'ã' or 'ä' or 'Á' or 'À' or 'Â' or 'Ã' or 'Ä' => 'A',
        'é' or 'è' or 'ê' or 'ë' or 'É' or 'È' or 'Ê' or 'Ë' => 'E',
        'í' or 'ì' or 'î' or 'ï' or 'Í' or 'Ì' or 'Î' or 'Ï' => 'I',
        'ó' or 'ò' or 'ô' or 'õ' or 'ö' or 'Ó' or 'Ò' or 'Ô' or 'Õ' or 'Ö' => 'O',
        'ú' or 'ù' or 'û' or 'ü' or 'Ú' or 'Ù' or 'Û' or 'Ü' => 'U',
        'ç' or 'Ç' => 'C',
        'ñ' or 'Ñ' => 'N',
        _ => char.ToUpperInvariant(c),
    };

    /// <summary>Linha da página: as palavras na mesma altura, da esquerda para a direita, e o texto normalizado com o início de cada palavra.</summary>
    internal sealed class Line
    {
        public required int Page { get; init; }

        public required List<PdfWord> Words { get; init; }

        public string Norm { get; private set; } = string.Empty;

        public string Text { get; private set; } = string.Empty;

        public List<int> Starts { get; } = [];

        public double Bottom => Words.Min(w => w.Bottom);

        public double Top => Words.Max(w => w.Top);

        public void Build()
        {
            Words.Sort((a, b) => a.Left.CompareTo(b.Left));
            var norm = new StringBuilder();
            var text = new StringBuilder();
            foreach (var word in Words)
            {
                if (norm.Length > 0)
                {
                    norm.Append(' ');
                    text.Append(' ');
                }

                Starts.Add(norm.Length);
                norm.Append(Normalize(word.Text));
                text.Append(word.Text);
            }

            Norm = norm.ToString();
            Text = text.ToString();
        }

        /// <summary>Índice da palavra que contém o caractere <paramref name="position"/> do texto normalizado.</summary>
        public int WordAt(int position)
        {
            var index = Starts.BinarySearch(position);
            return index >= 0 ? index : ~index - 1;
        }
    }

    internal static List<Line> Lines(IReadOnlyList<PdfWord> words)
    {
        var lines = new List<Line>();
        foreach (var page in words.Where(w => !string.IsNullOrWhiteSpace(w.Text)).GroupBy(w => w.Page).OrderBy(g => g.Key))
        {
            var pageLines = new List<Line>();
            foreach (var word in page.OrderByDescending(w => w.Bottom).ThenBy(w => w.Left))
            {
                var line = pageLines.LastOrDefault();
                if (line is not null && Math.Abs(line.Words[0].Bottom - word.Bottom) <= 2.5)
                {
                    line.Words.Add(word);
                }
                else
                {
                    pageLines.Add(new Line { Page = page.Key, Words = [word] });
                }
            }

            pageLines.ForEach(l => l.Build());
            lines.AddRange(pageLines);
        }

        return lines;
    }

    /// <summary>
    /// Valor do quadro do rótulo: logo depois do rótulo na mesma linha ou na linha de baixo, debaixo do rótulo (as duas linhas mais
    /// próximas). <paramref name="notFollowedBy"/> evita "VALOR DO ICMS" achar "VALOR DO ICMS ST".
    /// </summary>
    private static string? LabelValue(List<Line> lines, string[] labels, string[] notFollowedBy, int from, int to, Func<string, string?> parse)
    {
        for (var i = from; i < Math.Min(to, lines.Count); i++)
        {
            var line = lines[i];
            foreach (var label in labels)
            {
                var at = FindLabel(line, label, notFollowedBy);
                if (at is not { } match)
                {
                    continue;
                }

                var (first, last) = match;
                if (last + 1 < line.Words.Count && parse(line.Words[last + 1].Text) is { } same)
                {
                    return same;
                }

                var left = line.Words[first].Left;
                var right = line.Words[last].Right;
                var bottom = line.Words[first].Bottom;
                var below = lines.Skip(i + 1).Where(l => l.Page == line.Page && l.Top <= bottom + 1 && bottom - l.Top < 30).Take(2);
                foreach (var next in below)
                {
                    var inBox = next.Words.Where(w => w.Center >= left - 8 && w.Center <= right + 8 || w.Left < right && w.Right > left).ToList();
                    if (inBox.Count > 0 && parse(string.Join(" ", inBox.Select(w => w.Text))) is { } joined)
                    {
                        return joined;
                    }

                    foreach (var word in inBox)
                    {
                        if (parse(word.Text) is { } single)
                        {
                            return single;
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Primeira e última palavra do rótulo na linha (palavra inteira), ou null.</summary>
    private static (int First, int Last)? FindLabel(Line line, string label, string[] notFollowedBy)
    {
        var start = 0;
        while (start < line.Norm.Length)
        {
            var at = line.Norm.IndexOf(label, start, StringComparison.Ordinal);
            if (at < 0)
            {
                return null;
            }

            var end = at + label.Length;
            var boundaryBefore = at == 0 || line.Norm[at - 1] == ' ';
            var boundaryAfter = end >= line.Norm.Length || line.Norm[end] is ' ' or ':' or '/' or '.';
            var rest = end >= line.Norm.Length ? string.Empty : line.Norm[end..].TrimStart(' ', ':', '/', '-');
            var follower = rest.Split(' ', 2)[0];
            if (boundaryBefore && boundaryAfter && !notFollowedBy.Contains(follower, StringComparer.Ordinal))
            {
                return (line.WordAt(at), line.WordAt(end - 1));
            }

            start = at + 1;
        }

        return null;
    }

    /// <summary>Chave de 44 dígitos (em blocos de 4 ou corrida): a primeira com o DV certo; sem nenhuma certa, a primeira achada como incerta.</summary>
    private static (string? Value, string State) AccessKey(List<Line> lines)
    {
        string? fallback = null;
        foreach (var line in lines)
        {
            foreach (Match m in KeyPattern().Matches(line.Text))
            {
                var digits = new string(m.Value.Where(char.IsAsciiDigit).ToArray());
                if (digits.Length != 44)
                {
                    continue;
                }

                if (HasValidDigit(digits))
                {
                    return (digits, Read);
                }

                fallback ??= digits;
            }
        }

        return fallback is null ? (null, NotFound) : (fallback, Uncertain);
    }

    /// <summary>DV da chave (módulo 11, pesos 2 a 9). A API confere de novo: aqui só escolhe a candidata.</summary>
    public static bool HasValidDigit(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != 44 || !key.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        var weight = 2;
        for (var i = 42; i >= 0; i--)
        {
            sum += (key[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var rest = sum % 11;
        return key[43] - '0' == (rest < 2 ? 0 : 11 - rest);
    }

    /// <summary>Emitente pelo canhoto ("RECEBEMOS DE … OS PRODUTOS") ou pela linha depois de "IDENTIFICAÇÃO DO EMITENTE"; sempre incerto.</summary>
    private static string? IssuerName(List<Line> lines)
    {
        foreach (var line in lines.Take(40))
        {
            var m = ReceivedFrom().Match(line.Text);
            if (m.Success && Clean(m.Groups[1].Value) is { Length: >= 2 } name)
            {
                return name;
            }
        }

        var at = lines.FindIndex(l => l.Norm.Contains("IDENTIFICACAO DO EMITENTE", StringComparison.Ordinal));
        return at >= 0 && at + 1 < lines.Count && Clean(lines[at + 1].Text) is { Length: >= 2 } next ? next : null;
    }

    private static string? Clean(string text)
    {
        var value = Spaces().Replace(text, " ").Trim();
        return value.Length == 0 ? null : value.Length > 200 ? value[..200] : value;
    }

    /// <summary>CNPJ do emitente no texto (antes do quadro do destinatário): só para conferir contra a chave.</summary>
    private static string? IssuerDocument(List<Line> lines, int until)
    {
        var labelled = LabelValue(lines, ["CNPJ"], [], 0, until, ParseCnpj);
        return labelled ?? lines.Take(until).SelectMany(l => l.Words).Select(w => ParseCnpj(w.Text)).FirstOrDefault(c => c is not null);
    }

    /// <summary>CNPJ do destinatário: o quadro "CNPJ/CPF" do destinatário (lido) ou o primeiro CNPJ do quadro (incerto).</summary>
    private static ReadField Recipient(List<Line> lines, int recipientAt)
    {
        if (recipientAt < 0)
        {
            return new ReadField("recipientDocument", null, NotFound);
        }

        var until = Math.Min(lines.Count, recipientAt + 8);
        if (LabelValue(lines, ["CNPJ/CPF", "CNPJ / CPF", "CNPJ"], [], recipientAt, until, ParseCnpj) is { } labelled)
        {
            return new ReadField("recipientDocument", labelled, Read);
        }

        var first = lines.Skip(recipientAt).Take(until - recipientAt).SelectMany(l => l.Words).Select(w => ParseCnpj(w.Text)).FirstOrDefault(c => c is not null);
        return first is null ? new ReadField("recipientDocument", null, NotFound) : new ReadField("recipientDocument", first, Uncertain);
    }

    /// <summary>Coluna da tabela de itens, achada pelo cabeçalho.</summary>
    private sealed record Column(string Kind, double Left, double Right)
    {
        public double Center => (Left + Right) / 2;
    }

    private sealed class ItemRow
    {
        public string? Code { get; set; }

        public StringBuilder Description { get; } = new();

        public string? Ncm { get; set; }

        public string? Unit { get; set; }

        public decimal? Quantity { get; set; }

        public decimal? UnitPrice { get; set; }

        public decimal? Amount { get; set; }

        public IEnumerable<ReadField> Fields(int index)
        {
            string K(string field) => string.Create(CultureInfo.InvariantCulture, $"items.{index}.{field}");
            var description = Clean(Description.ToString());
            var consistent = Quantity is { } q && UnitPrice is { } u && Amount is { } a && Math.Abs(Math.Round(q * u, 2, MidpointRounding.AwayFromZero) - a) <= 0.01m;
            string NumberState(decimal? v) => v is null ? NotFound : consistent ? Read : Uncertain;
            yield return new ReadField(K("code"), Code, Code is null ? NotFound : Read);
            yield return new ReadField(K("description"), description is { Length: > 120 } d ? d[..120] : description, description is null ? NotFound : Read);
            yield return new ReadField(K("ncm"), Ncm, Ncm is null ? NotFound : Ncm.Length == 8 && Ncm.All(char.IsAsciiDigit) ? Read : Uncertain);
            yield return new ReadField(K("unit"), Unit, Unit is null ? NotFound : Unit.Length <= 6 ? Read : Uncertain);
            yield return new ReadField(K("quantity"), Quantity?.ToString(CultureInfo.InvariantCulture), NumberState(Quantity));
            yield return new ReadField(K("unitPrice"), UnitPrice?.ToString(CultureInfo.InvariantCulture), NumberState(UnitPrice));
            yield return new ReadField(K("amount"), Amount?.ToString(CultureInfo.InvariantCulture), NumberState(Amount));
        }
    }

    /// <summary>Itens: o cabeçalho com DESCRIÇÃO, NCM e QUANT. em cada página; uma linha com quantidade e valor abre o item, as outras continuam a descrição.</summary>
    private static List<ItemRow> Items(List<Line> lines)
    {
        var items = new List<ItemRow>();
        for (var i = 0; i < lines.Count && items.Count < MaxItems; i++)
        {
            var header = lines[i];
            if (!(header.Norm.Contains("DESCRI", StringComparison.Ordinal) && header.Norm.Contains("NCM", StringComparison.Ordinal)
                && (header.Norm.Contains("QUANT", StringComparison.Ordinal) || header.Norm.Contains("QTD", StringComparison.Ordinal))))
            {
                continue;
            }

            var headerWords = new List<PdfWord>(header.Words);
            var next = i + 1 < lines.Count ? lines[i + 1] : null;
            if (next is not null && next.Page == header.Page && header.Bottom - next.Top < 4 && !next.Words.Any(w => ParseNumber(w.Text) is not null))
            {
                headerWords.AddRange(next.Words);
                i++;
            }

            var columns = Columns(headerWords);
            if (!columns.Any(c => c.Kind == "amount") || !columns.Any(c => c.Kind == "quantity"))
            {
                continue;
            }

            ItemRow? current = null;
            for (i++; i < lines.Count && lines[i].Page == header.Page; i++)
            {
                var line = lines[i];
                if (StopTable.Any(s => line.Norm.Contains(s, StringComparison.Ordinal)))
                {
                    break;
                }

                var cells = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var word in line.Words)
                {
                    var kind = ColumnOf(columns, word.Center);
                    if (!cells.TryGetValue(kind, out var list))
                    {
                        cells[kind] = list = [];
                    }

                    list.Add(word.Text);
                }

                string? Cell(string kind) => cells.TryGetValue(kind, out var v) ? string.Join(" ", v) : null;
                var quantity = Cell("quantity") is { } qt ? ParseNumber(qt) : null;
                var amount = Cell("amount") is { } at ? ParseNumber(at) : null;
                if (quantity is not null && amount is not null)
                {
                    current = new ItemRow
                    {
                        Code = Clean(Cell("code") ?? string.Empty) is { } code ? code.Length > 60 ? code[..60] : code : null,
                        Ncm = Cell("ncm") is { } ncm ? new string(ncm.Where(char.IsAsciiDigit).ToArray()) is { Length: > 0 } n ? n : ncm.Trim() : null,
                        Unit = Clean(Cell("unit") ?? string.Empty),
                        Quantity = decimal.Parse(quantity, NumberStyles.Number, CultureInfo.InvariantCulture),
                        UnitPrice = Cell("unitPrice") is { } up && ParseNumber(up) is { } price ? decimal.Parse(price, NumberStyles.Number, CultureInfo.InvariantCulture) : null,
                        Amount = decimal.Parse(amount, NumberStyles.Number, CultureInfo.InvariantCulture),
                    };
                    current.Description.Append(Cell("description"));
                    items.Add(current);
                    if (items.Count >= MaxItems)
                    {
                        break;
                    }
                }
                else if (current is not null && Cell("description") is { } more && cells.Keys.All(k => k == "description"))
                {
                    current.Description.Append(' ').Append(more);
                }
            }

            i--;
        }

        return items;
    }

    /// <summary>Colunas pelo cabeçalho: palavras próximas (ou empilhadas em duas linhas) formam o rótulo de uma coluna.</summary>
    private static List<Column> Columns(List<PdfWord> words)
    {
        var groups = new List<(double Left, double Right, List<PdfWord> Words)>();
        foreach (var word in words.OrderBy(w => w.Left))
        {
            var match = groups.FindIndex(g => word.Left <= g.Right + 3 && word.Right >= g.Left - 3);
            if (match >= 0)
            {
                var g = groups[match];
                g.Words.Add(word);
                groups[match] = (Math.Min(g.Left, word.Left), Math.Max(g.Right, word.Right), g.Words);
            }
            else
            {
                groups.Add((word.Left, word.Right, [word]));
            }
        }

        var columns = new List<Column>();
        var amountTaken = false;
        foreach (var (left, right, list) in groups.OrderBy(g => g.Left))
        {
            var text = Normalize(string.Join(" ", list.OrderByDescending(w => w.Top).ThenBy(w => w.Left).Select(w => w.Text)));
            var kind = text switch
            {
                _ when text.Contains("DESCRI", StringComparison.Ordinal) => "description",
                _ when text.StartsWith("COD", StringComparison.Ordinal) => "code",
                _ when text.Contains("NCM", StringComparison.Ordinal) => "ncm",
                _ when text.Contains("CFOP", StringComparison.Ordinal) => "cfop",
                _ when text.Contains("CST", StringComparison.Ordinal) || text.Contains("CSOSN", StringComparison.Ordinal) => "cst",
                _ when text.Contains("QUANT", StringComparison.Ordinal) || text.Contains("QTD", StringComparison.Ordinal) => "quantity",
                _ when text.Contains("UNIT", StringComparison.Ordinal) => "unitPrice",
                "UN" or "UN." or "UNID" or "UNID." or "UNIDADE" or "UND" or "UNID. COM" => "unit",
                _ when !amountTaken && text.Contains("TOTAL", StringComparison.Ordinal) && !text.Contains("ICMS", StringComparison.Ordinal) && !text.Contains("IPI", StringComparison.Ordinal) => "amount",
                _ => "other",
            };
            amountTaken |= kind == "amount";
            columns.Add(new Column(kind, left, right));
        }

        return columns;
    }

    /// <summary>A coluna do valor: a do intervalo entre os pontos médios dos rótulos vizinhos.</summary>
    private static string ColumnOf(List<Column> columns, double center)
    {
        for (var c = 0; c < columns.Count; c++)
        {
            var start = c == 0 ? double.MinValue : (columns[c - 1].Right + columns[c].Left) / 2;
            var end = c == columns.Count - 1 ? double.MaxValue : (columns[c].Right + columns[c + 1].Left) / 2;
            if (center >= start && center < end)
            {
                return columns[c].Kind;
            }
        }

        return "other";
    }

    /// <summary>Valor em reais no formato da DANFE ("1.234,56"), devolvido com ponto ("1234.56"); null se não for dinheiro.</summary>
    public static string? ParseMoney(string text)
    {
        var value = text.Replace("R$", string.Empty, StringComparison.Ordinal).Trim();
        return MoneyPattern().IsMatch(value) ? Invariant(value) : null;
    }

    /// <summary>Número da DANFE (quantidade, preço): "10,0000", "1.234,5600000000" ou inteiro.</summary>
    public static string? ParseNumber(string text)
    {
        var value = text.Replace("R$", string.Empty, StringComparison.Ordinal).Trim();
        return NumberPattern().IsMatch(value) ? Invariant(value) : null;
    }

    private static string? Invariant(string ptBr)
    {
        var plain = ptBr.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        return decimal.TryParse(plain, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) && d < 1_000_000_000_000m ? d.ToString("0.##########", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Data dd/mm/aaaa, devolvida como aaaa-mm-dd.</summary>
    public static string? ParseDate(string text)
    {
        var m = DatePattern().Match(text);
        return m.Success && DateOnly.TryParseExact(m.Value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>CNPJ formatado ("12.345.678/0001-99") ou corrido com 14 dígitos, devolvido só com os dígitos.</summary>
    public static string? ParseCnpj(string text)
    {
        var m = CnpjPattern().Match(text);
        return m.Success ? new string(m.Value.Where(char.IsAsciiDigit).ToArray()) : null;
    }

    [GeneratedRegex(@"\d{4}(?:[ .]?\d{4}){10}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"RECEBEMOS DE (.+?) OS PRODUTOS", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex ReceivedFrom();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"^(?:\d{1,3}(?:\.\d{3})+|\d+),\d{2,10}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex MoneyPattern();

    [GeneratedRegex(@"^(?:(?:\d{1,3}(?:\.\d{3})+|\d+)(?:,\d{1,10})?)$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\b\d{2}/\d{2}/\d{4}\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex DatePattern();

    [GeneratedRegex(@"\b\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}\b|^\d{14}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex CnpjPattern();
}
