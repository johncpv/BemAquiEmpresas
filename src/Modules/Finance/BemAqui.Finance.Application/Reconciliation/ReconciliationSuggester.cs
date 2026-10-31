using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Application.Reconciliation;

/// <summary>Linha do extrato, como o motor de sugestão enxerga (valor com sinal).</summary>
public sealed record SuggestionLine(Guid Id, DateOnly Date, decimal Amount, string Description, string? CounterpartyDocument, string ExternalId, bool BankVerified);

/// <summary>
/// Transação do sistema, como o motor enxerga: os identificadores do boleto do Inter (seu número, nosso número, código da solicitação), o
/// documento do terceiro com o papel ("do cliente", "do fornecedor") e os números legíveis (título, OP) que podem aparecer na descrição.
/// </summary>
public sealed record SuggestionTransaction(
    Guid Id, DateOnly Date, decimal Amount, IReadOnlyList<string> BankReferences, string? PartyDocument, string? PartyRole, IReadOnlyList<string> DocumentNumbers);

/// <summary>Par sugerido: a linha, a transação, a confiança e o motivo em português.</summary>
public sealed record PairSuggestion(Guid LineId, Guid TransactionId, ReconciliationConfidence Confidence, string Reason);

/// <summary>
/// Motor de sugestão da conciliação (BNK-03b CA 1), puro: candidatos da mesma conta, nesta ordem: (a) boleto do Inter com o mesmo
/// identificador do banco; (b) mesmo valor e mesma data; (c) mesmo valor com até <see cref="WindowDays"/> dias de diferença e documento
/// ou contraparte parecidos. Uma transação nunca é sugerida para duas linhas; linha "não verificada pelo banco" nunca tem confiança Alta.
/// </summary>
public static class ReconciliationSuggester
{
    public const int WindowDays = 3;

    public const string SameBankId = "Boleto do Inter com o mesmo identificador do banco";

    /// <summary>Identificador curto demais casa por acaso: só a partir deste tamanho.</summary>
    private const int MinReferenceLength = 6;

    public static IReadOnlyList<PairSuggestion> Suggest(IEnumerable<SuggestionLine> lines, IEnumerable<SuggestionTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(transactions);
        var ordered = lines.Where(l => l.Amount != 0).OrderBy(l => l.Date).ThenBy(l => l.Id).ToList();
        var pool = transactions.OrderBy(t => t.Date).ThenBy(t => t.Id).ToList();
        var used = new HashSet<Guid>();
        var result = new Dictionary<Guid, PairSuggestion>();

        // (a) Mesmo identificador do banco (o boleto pago pelo Inter, BNK-02).
        foreach (var line in ordered)
        {
            var match = pool.Where(t => !used.Contains(t.Id) && t.Amount == line.Amount && SameReference(line, t))
                .OrderBy(t => Math.Abs(t.Date.DayNumber - line.Date.DayNumber)).FirstOrDefault();
            if (match is not null)
            {
                Take(line, match, ReconciliationConfidence.High, SameBankId);
            }
        }

        // (b) Mesmo valor e mesma data: Alta quando só há uma transação assim (ou o documento desempata).
        foreach (var line in ordered.Where(l => !result.ContainsKey(l.Id)))
        {
            var candidates = pool.Where(t => !used.Contains(t.Id) && t.Amount == line.Amount && t.Date == line.Date).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var withDocument = candidates.Select(t => (Transaction: t, Why: DocumentReason(line, t))).Where(c => c.Why is not null).ToList();
            if (withDocument.Count > 0)
            {
                Take(line, withDocument[0].Transaction, ReconciliationConfidence.High, "Mesmo valor e mesma data, " + withDocument[0].Why);
            }
            else if (candidates.Count == 1)
            {
                Take(line, candidates[0], ReconciliationConfidence.High, "Mesmo valor e mesma data");
            }
            else
            {
                Take(line, candidates[0], ReconciliationConfidence.Medium, $"Mesmo valor e mesma data (há {candidates.Count} transações iguais)");
            }
        }

        // (c) Mesmo valor, até 3 dias de diferença e documento ou contraparte parecidos.
        foreach (var line in ordered.Where(l => !result.ContainsKey(l.Id)))
        {
            var match = pool.Where(t => !used.Contains(t.Id) && t.Amount == line.Amount && Math.Abs(t.Date.DayNumber - line.Date.DayNumber) is > 0 and <= WindowDays)
                .Select(t => (Transaction: t, Why: DocumentReason(line, t), Days: Math.Abs(t.Date.DayNumber - line.Date.DayNumber)))
                .Where(c => c.Why is not null).OrderBy(c => c.Days).FirstOrDefault();
            if (match.Transaction is not null)
            {
                Take(line, match.Transaction, ReconciliationConfidence.Medium, $"Mesmo valor, {match.Days} {(match.Days == 1 ? "dia" : "dias")} de diferença, {match.Why}");
            }
        }

        return [.. ordered.Where(l => result.ContainsKey(l.Id)).Select(l => result[l.Id])];

        void Take(SuggestionLine line, SuggestionTransaction transaction, ReconciliationConfidence confidence, string reason)
        {
            used.Add(transaction.Id);
            // CA 1: linha "não verificada pelo banco" (OFX de contingência) nunca tem confiança Alta.
            var capped = line.BankVerified ? confidence : ReconciliationConfidence.Medium;
            result[line.Id] = new PairSuggestion(line.Id, transaction.Id, capped, line.BankVerified ? reason : reason + "; linha não verificada pelo banco");
        }
    }

    private static bool SameReference(SuggestionLine line, SuggestionTransaction transaction) =>
        transaction.BankReferences.Any(r => r is { Length: >= MinReferenceLength }
            && (string.Equals(line.ExternalId, r, StringComparison.OrdinalIgnoreCase) || line.Description.Contains(r, StringComparison.OrdinalIgnoreCase)));

    /// <summary>"CNPJ do fornecedor", "CPF do cliente" ou "número OP-2026-0003 na descrição"; nulo sem semelhança.</summary>
    public static string? DocumentReason(SuggestionLine line, SuggestionTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(transaction);
        var bank = BrazilianDocuments.Normalize(line.CounterpartyDocument);
        var party = BrazilianDocuments.Normalize(transaction.PartyDocument);
        if (bank.Length is 11 or 14 && bank == party)
        {
            return (bank.Length == 14 ? "CNPJ " : "CPF ") + (transaction.PartyRole ?? "do terceiro");
        }

        var number = transaction.DocumentNumbers.FirstOrDefault(n => n is { Length: >= 4 } && line.Description.Contains(n, StringComparison.OrdinalIgnoreCase));
        return number is null ? null : $"número {number} na descrição";
    }
}

/// <summary>Regras puras da conciliação: soma do vínculo (CA 4) e o tipo do lançamento sem OP a partir da linha (CA 2 e 8).</summary>
public static class ReconciliationRules
{
    /// <summary>Saída que pode virar lançamento sem OP (D4): tarifa, IOF e juros. "Outra saída" nunca sai da conciliação (CA 8).</summary>
    public static readonly IReadOnlySet<BankEntryKind> OutflowKinds = new HashSet<BankEntryKind> { BankEntryKind.BankFee, BankEntryKind.Iof, BankEntryKind.InterestPaid };

    public static readonly IReadOnlySet<BankEntryKind> InflowKinds = new HashSet<BankEntryKind> { BankEntryKind.InterestReceived, BankEntryKind.Yield, BankEntryKind.OtherIn };

    /// <summary>
    /// Tipo do lançamento sem OP para a linha: a primeira regra do tenant que vale (palavra e/ou tipo do banco, com o sinal certo) e, sem
    /// regra, o tipo que o próprio banco deu (tarifa, IOF, juros e rendimento). Nulo: a linha não é tarifa, IOF, juros nem rendimento.
    /// </summary>
    public static (BankEntryKind Kind, BankStatementRule? Rule)? EntryKindFor(string description, StatementLineKind kind, decimal amount, IEnumerable<BankStatementRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.FirstOrDefault(r => r.Matches(description, kind, amount)) is { } rule && (amount < 0 ? OutflowKinds : InflowKinds).Contains(rule.EntryKind))
        {
            return (rule.EntryKind, rule);
        }

        BankEntryKind? byBank = (kind, amount < 0) switch
        {
            (StatementLineKind.Fee, true) => BankEntryKind.BankFee,
            (StatementLineKind.Tax, true) when (description ?? string.Empty).Contains("IOF", StringComparison.OrdinalIgnoreCase) => BankEntryKind.Iof,
            (StatementLineKind.Interest, true) => BankEntryKind.InterestPaid,
            (StatementLineKind.Interest, false) => BankEntryKind.InterestReceived,
            (StatementLineKind.Yield, false) => BankEntryKind.Yield,
            _ => null,
        };
        return byBank is { } k ? (k, null) : null;
    }

    /// <summary>
    /// O tipo pedido pode sair desta linha? Entrada: juros recebidos, rendimento ou outra entrada. Saída: só tarifa, IOF ou juros, e só se a
    /// linha for reconhecida como tal (regra ou tipo do banco); o resto da saída é pagamento e exige OP aprovada (R-FIN-5, CA 8).
    /// </summary>
    public static bool EntryAllowed(BankEntryKind requested, decimal amount, (BankEntryKind Kind, BankStatementRule? Rule)? recognized) =>
        amount < 0 ? OutflowKinds.Contains(requested) && recognized is not null : amount > 0 && InflowKinds.Contains(requested);

    /// <summary>
    /// Soma do vínculo (CA 4): uma linha com uma ou mais transações, ou várias linhas com uma transação, e a soma igual. Nulo quando bate.
    /// </summary>
    public static string? SumError(IReadOnlyList<decimal> lines, IReadOnlyList<decimal> transactions)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(transactions);
        if (lines.Count == 0 || transactions.Count == 0)
        {
            return "Escolha a linha do banco e ao menos uma transação do sistema.";
        }

        if (lines.Count > 1 && transactions.Count > 1)
        {
            return "Concilie uma linha com várias transações, ou várias linhas com uma transação.";
        }

        var lineSum = lines.Sum();
        var transactionSum = transactions.Sum();
        if (lineSum == transactionSum)
        {
            return null;
        }

        return lines.Count == 1
            ? $"A soma das transações ({ReceivableService.Money(transactionSum)}) não bate com a linha ({ReceivableService.Money(lineSum)})."
            : $"A soma das linhas ({ReceivableService.Money(lineSum)}) não bate com a transação ({ReceivableService.Money(transactionSum)}).";
    }
}
