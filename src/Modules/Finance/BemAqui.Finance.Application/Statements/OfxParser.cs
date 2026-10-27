using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application.Statements;

/// <summary>Limites do parser (BNK-03a CA 4): tamanho, número de lançamentos e tempo.</summary>
public sealed record OfxLimits(long MaxBytes, int MaxTransactions, TimeSpan Timeout)
{
    public static readonly OfxLimits Default = new(5L * 1024 * 1024, 20_000, TimeSpan.FromSeconds(10));
}

/// <summary>Lançamento lido do OFX (<c>STMTTRN</c>), na ordem do arquivo.</summary>
public sealed record OfxTransaction(int Sequence, string? FitId, DateOnly Date, decimal Amount, string? TransactionType, string? Name, string? Memo);

/// <summary>Extrato lido do OFX: a conta (<c>BANKACCTFROM</c>), o período, os lançamentos e o saldo (<c>LEDGERBAL</c>).</summary>
public sealed record OfxStatement(
    string Version, string? BankId, string? BranchId, string? AccountId, DateOnly? Start, DateOnly? End, IReadOnlyList<OfxTransaction> Transactions, decimal? LedgerBalance, DateOnly? LedgerDate);

/// <summary>
/// Parser OFX próprio e restrito (BNK-03a CA 4, ADR 0032 §7, revisão pré-Fase 0 §3): OFX 1.x (SGML, sem tag de fechamento nas folhas) e
/// 2.x (XML com <see cref="DtdProcessing.Prohibit"/>, sem resolver de entidades externas). Recusa declaração DTD ou de entidade, arquivo
/// acima do limite de tamanho ou de lançamentos e leitura acima do tempo limite. Nunca devolve o conteúdo do arquivo na mensagem.
/// </summary>
public static class OfxParser
{
    public const string NotOfx = "O arquivo não é um extrato OFX. Exporte o extrato no formato OFX (Money) pelo internet banking.";
    public const string DtdRefused = "O arquivo traz uma declaração DTD ou de entidade, recusada por segurança. Exporte de novo pelo internet banking.";
    public const string TimedOut = "A leitura do arquivo passou do tempo limite. Exporte um período menor.";
    public const string NoStatement = "O arquivo não traz o extrato de uma conta corrente (BANKMSGSRSV1). Exporte o extrato da conta, não o do cartão.";
    public const string ManyAccounts = "O arquivo traz o extrato de mais de uma conta. Exporte uma conta por arquivo.";

    /// <summary>Agregados do OFX 1.x (fecham com tag própria); folha vazia sem fechamento não abre nível.</summary>
    private static readonly HashSet<string> Aggregates = new(StringComparer.Ordinal)
    {
        "OFX", "SIGNONMSGSRSV1", "SONRS", "STATUS", "FI", "BANKMSGSRSV1", "STMTTRNRS", "STMTRS", "BANKACCTFROM", "BANKACCTTO", "BANKTRANLIST", "STMTTRN",
        "LEDGERBAL", "AVAILBAL", "PAYEE", "CURRENCY", "ORIGCURRENCY", "BALLIST", "BAL", "CREDITCARDMSGSRSV1", "CCSTMTTRNRS", "CCSTMTRS", "CCACCTFROM", "CCACCTTO",
    };

    public static string TooLarge(OfxLimits limits) => $"O arquivo precisa ter até {(limits ?? OfxLimits.Default).MaxBytes / 1024 / 1024} MB.";

    public static string TooMany(OfxLimits limits) => $"O arquivo tem mais de {(limits ?? OfxLimits.Default).MaxTransactions.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} lançamentos. Exporte um período menor.";

    /// <summary>Confere só o início do arquivo (magic bytes do dono <c>BankStatementBatch</c>): cabeçalho OFX 1.x ou XML com <c>OFX</c>.</summary>
    public static bool LooksLikeOfx(ReadOnlySpan<byte> head) => BemAqui.BuildingBlocks.Attachments.AttachmentPolicy.IsOfx(head);

    public static Result<OfxStatement> Parse(byte[] content, OfxLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        limits ??= OfxLimits.Default;
        if (content.Length == 0 || content.Length > limits.MaxBytes)
        {
            return Invalid("ofx_size", content.Length == 0 ? NotOfx : TooLarge(limits));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (limits.Timeout > TimeSpan.Zero)
        {
            timeout.CancelAfter(limits.Timeout);
        }
        else
        {
            timeout.Cancel();
        }
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            var text = Decode(content);
            var trimmed = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
            Node root;
            string version;
            if (trimmed.StartsWith("OFXHEADER:", StringComparison.OrdinalIgnoreCase))
            {
                version = Header(trimmed, "VERSION") ?? "1";
                var start = trimmed.IndexOf("<OFX>", StringComparison.OrdinalIgnoreCase);
                if (start < 0)
                {
                    return Invalid("ofx_invalid", NotOfx);
                }

                var body = trimmed[start..];
                if (trimmed.Contains("<!", StringComparison.Ordinal))
                {
                    return Invalid("ofx_dtd", DtdRefused);
                }

                root = Sgml(body, timeout.Token);
            }
            else if (trimmed.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                if (trimmed.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
                {
                    return Invalid("ofx_dtd", DtdRefused);
                }

                version = "2";
                root = Xml(trimmed, limits, timeout.Token);
            }
            else
            {
                return Invalid("ofx_invalid", NotOfx);
            }

            return Extract(root, version, limits, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Invalid("ofx_timeout", TimedOut);
        }
        catch (XmlException ex) when (ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("entity", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid("ofx_dtd", DtdRefused);
        }
        catch (XmlException)
        {
            return Invalid("ofx_invalid", NotOfx);
        }
        catch (FormatException)
        {
            return Invalid("ofx_invalid", NotOfx);
        }
    }

    /// <summary>Id da linha sem <c>FITID</c> (CA 5): hash de data, valor, descrição e ordem no arquivo.</summary>
    public static string SyntheticId(OfxTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var key = string.Join('|', transaction.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), transaction.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            (transaction.Memo ?? transaction.Name ?? string.Empty).Trim(), transaction.Sequence.ToString(CultureInfo.InvariantCulture));
        return "sem-fitid:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..40];
    }

    /// <summary>Id do banco gravado: o <c>FITID</c> como veio (até 100) ou o hash dele quando é maior.</summary>
    public static string ExternalId(OfxTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.FitId is not { Length: > 0 } fit)
        {
            return SyntheticId(transaction);
        }

        return fit.Length <= 100 ? fit : "fitid-sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fit)));
    }

    private static Result<OfxStatement> Extract(Node root, string version, OfxLimits limits, CancellationToken ct)
    {
        var statements = root.Descendants("STMTRS").ToList();
        if (statements.Count == 0)
        {
            return Invalid("ofx_no_statement", NoStatement);
        }

        if (statements.Count > 1)
        {
            return Invalid("ofx_many_accounts", ManyAccounts);
        }

        var statement = statements[0];
        var from = statement.Child("BANKACCTFROM");
        var list = statement.Child("BANKTRANLIST");
        var entries = list?.Children.Where(c => c.Name == "STMTTRN").ToList() ?? [];
        if (entries.Count > limits.MaxTransactions)
        {
            return Invalid("ofx_too_many", TooMany(limits));
        }

        var transactions = new List<OfxTransaction>(entries.Count);
        var sequence = 0;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            sequence++;
            var date = Date(entry.Value("DTPOSTED")) ?? Date(entry.Value("DTUSER"));
            var amount = Amount(entry.Value("TRNAMT"));
            if (date is null || amount is null)
            {
                return Invalid("ofx_invalid_transaction", $"O lançamento {sequence} do arquivo não tem data ou valor válido. Exporte o arquivo de novo.");
            }

            transactions.Add(new OfxTransaction(sequence, Clean(entry.Value("FITID"), 255), date.Value, amount.Value, Clean(entry.Value("TRNTYPE"), 20),
                Clean(entry.Value("NAME") ?? entry.Child("PAYEE")?.Value("NAME"), 150), Clean(entry.Value("MEMO"), 300)));
        }

        var ledger = statement.Child("LEDGERBAL");
        return new OfxStatement(version, Clean(from?.Value("BANKID"), 20), Clean(from?.Value("BRANCHID"), 20), Clean(from?.Value("ACCTID"), 40),
            Date(list?.Value("DTSTART")), Date(list?.Value("DTEND")), transactions, Amount(ledger?.Value("BALAMT")), Date(ledger?.Value("DTASOF")));
    }

    private static Error Invalid(string code, string message) => Error.Validation(code, message, new Dictionary<string, string[]> { ["file"] = [message] });

    /// <summary>UTF-8 quando o conteúdo é UTF-8 válido; senão Latin-1 (o CHARSET 1252 dos bancos brasileiros, sem o provedor de code pages).</summary>
    private static string Decode(byte[] content)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    private static string? Header(string text, string key)
    {
        foreach (var line in text.Split('\n').Take(20))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[(key.Length + 1)..].Trim();
            }
        }

        return null;
    }

    /// <summary>
    /// SGML do OFX 1.x: folha é <c>&lt;TAG&gt;valor</c> (o fechamento é opcional); agregado fecha com <c>&lt;/TAG&gt;</c>. Fechamento sem
    /// abertura correspondente é ignorado; ao fechar, as folhas e agregados abertos dentro dele fecham juntos.
    /// </summary>
    private static Node Sgml(string body, CancellationToken ct)
    {
        var root = new Node("#root");
        var stack = new Stack<Node>();
        stack.Push(root);
        var i = 0;
        var steps = 0;
        while (i < body.Length)
        {
            if (++steps % 512 == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            var open = body.IndexOf('<', i);
            if (open < 0)
            {
                break;
            }

            var close = body.IndexOf('>', open);
            if (close < 0)
            {
                throw new FormatException("Tag sem fechamento.");
            }

            var tag = body[(open + 1)..close].Trim();
            var next = body.IndexOf('<', close + 1);
            var text = (next < 0 ? body[(close + 1)..] : body[(close + 1)..next]).Trim();
            i = next < 0 ? body.Length : next;
            if (tag.Length == 0)
            {
                continue;
            }

            if (tag[0] == '/')
            {
                var name = tag[1..].Trim().ToUpperInvariant();
                if (stack.Any(n => n.Name == name))
                {
                    while (stack.Peek().Name != name)
                    {
                        stack.Pop();
                    }

                    stack.Pop();
                }

                continue;
            }

            var node = new Node(tag.Split(' ', 2)[0].ToUpperInvariant());
            stack.Peek().Children.Add(node);
            if (text.Length > 0)
            {
                node.Text = Unescape(text);
            }
            else if (Aggregates.Contains(node.Name) || (next >= 0 && body.AsSpan(next).StartsWith("</" + node.Name, StringComparison.OrdinalIgnoreCase)))
            {
                // Agregado conhecido, ou folha vazia com fechamento logo depois (<MEMO></MEMO>): abre e o fechamento tira da pilha.
                stack.Push(node);
            }
        }

        return root;
    }

    private static Node Xml(string text, OfxLimits limits, CancellationToken ct)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 1024,
            MaxCharactersInDocument = limits.MaxBytes * 2,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };
        var root = new Node("#root");
        var stack = new Stack<Node>();
        stack.Push(root);
        using var reader = XmlReader.Create(new StringReader(text), settings);
        var steps = 0;
        while (reader.Read())
        {
            if (++steps % 512 == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    var node = new Node(reader.LocalName.ToUpperInvariant());
                    stack.Peek().Children.Add(node);
                    if (!reader.IsEmptyElement)
                    {
                        stack.Push(node);
                    }

                    break;
                case XmlNodeType.Text or XmlNodeType.CDATA:
                    stack.Peek().Text = (stack.Peek().Text ?? string.Empty) + reader.Value;
                    break;
                case XmlNodeType.EndElement:
                    stack.Pop();
                    break;
            }
        }

        return root;
    }

    private static string Unescape(string text) =>
        text.Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal).Replace("&nbsp;", " ", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal);

    private static string? Clean(string? value, int max)
    {
        if (value is null)
        {
            return null;
        }

        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        return cleaned.Length == 0 ? null : cleaned.Length > max ? cleaned[..max] : cleaned;
    }

    /// <summary>Data do OFX (<c>AAAAMMDD[HHMMSS[.XXX]][[-3:BRT]]</c>): os 8 primeiros dígitos, a data no fuso do banco.</summary>
    internal static DateOnly? Date(string? value) =>
        value is { Length: >= 8 } && DateOnly.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>Valor com ponto ou vírgula decimal (bancos brasileiros usam os dois), até 2 casas.</summary>
    internal static decimal? Amount(string? value)
    {
        if (value is not { Length: > 0 })
        {
            return null;
        }

        var text = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (text.Contains(',', StringComparison.Ordinal))
        {
            text = text.Contains('.', StringComparison.Ordinal) ? text.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.') : text.Replace(',', '.');
        }

        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || Math.Abs(amount) >= 10_000_000_000_000_000m)
        {
            return null;
        }

        return decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    }

    private sealed class Node(string name)
    {
        public string Name { get; } = name;

        public string? Text { get; set; }

        public List<Node> Children { get; } = [];

        public Node? Child(string name) => Children.FirstOrDefault(c => c.Name == name);

        public string? Value(string name) => Child(name)?.Text?.Trim();

        public IEnumerable<Node> Descendants(string name)
        {
            foreach (var child in Children)
            {
                if (child.Name == name)
                {
                    yield return child;
                }

                foreach (var nested in child.Descendants(name))
                {
                    yield return nested;
                }
            }
        }
    }
}
