using System.Text.Json.Serialization;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>Tipo da linha do DRE (FIN-08 CA 1): grupo da DRE, subtotal, conta do plano ou linha de memória (fora do resultado).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DreRowKind>))]
public enum DreRowKind
{
    Group,
    Subtotal,
    Account,
    Memo,
}

/// <summary>Conta do plano para a montagem do DRE (inclusive inativa ou excluída: documento antigo continua no mês dele).</summary>
public sealed record DreAccount(Guid Id, string Code, string Name, Guid? ParentId, DreGroup Group);

/// <summary>
/// Valor com sinal de uma conta num mês (primeiro dia do mês): <b>positivo</b> é receita ou entrada (recebível, juros recebidos,
/// desconto obtido, lançamento de entrada) e <b>negativo</b> é custo, despesa ou saída. O sinal vem do documento, não da conta.
/// </summary>
public sealed record DreAmount(Guid AccountId, DateOnly Month, decimal Value);

/// <summary>
/// Linha do DRE: os valores já vêm como a tela mostra (padrão 3 da revisão pré-Fases 4 a 8): o sinal fica no rótulo, então
/// "(−) Custos" mostra o valor positivo. <see cref="CriticalWhenNegative"/> marca os resultados que ficam críticos quando
/// negativos (Lucro bruto, Resultado operacional e Resultado do período); nenhuma linha de despesa fica crítica.
/// </summary>
public sealed record DreRow(
    string Key,
    DreRowKind Kind,
    string Label,
    DreGroup? Group,
    Guid? AccountId,
    string? Code,
    string? ParentKey,
    int Level,
    IReadOnlyList<decimal> Values,
    decimal Total,
    decimal? PercentOfNetRevenue,
    decimal? Delta,
    decimal? DeltaPercent,
    bool CriticalWhenNegative,
    bool HasChildren);

public sealed record DreStatement(IReadOnlyList<DateOnly> Months, IReadOnlyList<DreRow> Rows)
{
    public DreRow Row(string key) => Rows.Single(r => r.Key == key);
}

/// <summary>
/// Montagem do DRE gerencial (FIN-08, R-FIN-13), pura: árvore das contas por grupo da DRE, subtotais, sinal no rótulo, % da
/// receita líquida e "Δ mês anterior" (último mês − o anterior). Contas do grupo "Fora da DRE" não entram.
/// <para>Receita líquida = Receita bruta − Deduções; Lucro bruto = Receita líquida − Custos; Resultado operacional = Lucro
/// bruto − Despesas operacionais; Resultado do período = Resultado operacional + Resultado financeiro + Outros resultados.</para>
/// </summary>
public static class DreBuilder
{
    public const string NetRevenue = "subtotal:NetRevenue";
    public const string GrossProfit = "subtotal:GrossProfit";
    public const string OperatingResult = "subtotal:OperatingResult";
    public const string PeriodResult = "subtotal:PeriodResult";
    public const string Advances = "memo:Advances";

    public static readonly IReadOnlyList<DreGroup> Groups =
        [DreGroup.GrossRevenue, DreGroup.Deductions, DreGroup.Costs, DreGroup.OperatingExpenses, DreGroup.FinancialResult, DreGroup.OtherResults];

    public static string GroupKey(DreGroup group) => "group:" + group;

    public static string AccountKey(Guid accountId) => "account:" + accountId.ToString("N");

    public static string GroupLabel(DreGroup group) => group switch
    {
        DreGroup.GrossRevenue => "Receita bruta",
        DreGroup.Deductions => "(−) Deduções",
        DreGroup.Costs => "(−) Custos",
        DreGroup.OperatingExpenses => "(−) Despesas operacionais",
        DreGroup.FinancialResult => "(±) Resultado financeiro",
        DreGroup.OtherResults => "(±) Outros resultados",
        _ => "Fora da DRE",
    };

    /// <summary>Fator de exibição: grupos "(−)" mostram o valor positivo (o sinal está no rótulo); os demais mostram o sinal real.</summary>
    public static int DisplayFactor(DreGroup group) => group is DreGroup.Deductions or DreGroup.Costs or DreGroup.OperatingExpenses ? -1 : 1;

    /// <summary>Primeiro dia do mês da data.</summary>
    public static DateOnly MonthOf(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>Meses de <paramref name="from"/> a <paramref name="to"/> (primeiro dia de cada), inclusive.</summary>
    public static IReadOnlyList<DateOnly> MonthsBetween(DateOnly from, DateOnly to)
    {
        var months = new List<DateOnly>();
        for (var m = MonthOf(from); m <= MonthOf(to); m = m.AddMonths(1))
        {
            months.Add(m);
        }

        return months;
    }

    /// <param name="advances">Saldo de "Adiantamentos de clientes (sinal ainda sem nota)" no fim de cada mês (CA 5), na ordem de <paramref name="months"/>.</param>
    public static DreStatement Build(IReadOnlyList<DateOnly> months, IReadOnlyCollection<DreAccount> accounts, IEnumerable<DreAmount> amounts, IReadOnlyList<decimal>? advances = null)
    {
        ArgumentNullException.ThrowIfNull(months);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(amounts);
        var index = months.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);
        var byId = accounts.ToDictionary(a => a.Id);
        var own = new Dictionary<Guid, decimal[]>();
        foreach (var amount in amounts)
        {
            if (!index.TryGetValue(MonthOf(amount.Month), out var i))
            {
                continue;
            }

            if (!byId.TryGetValue(amount.AccountId, out var account))
            {
                // Documento numa conta que não está mais no plano: não some do resultado, aparece em "Outros resultados".
                account = byId[amount.AccountId] = new DreAccount(amount.AccountId, "?", "Conta não encontrada no plano", null, DreGroup.OtherResults);
            }

            if (account.Group == DreGroup.OutsideDre)
            {
                continue;
            }

            var values = own.TryGetValue(amount.AccountId, out var v) ? v : own[amount.AccountId] = new decimal[months.Count];
            values[i] += amount.Value;
        }

        var signedGroups = new Dictionary<DreGroup, decimal[]>();
        var groupRows = new Dictionary<DreGroup, List<DreRow>>();
        decimal netRevenueTotal = 0;
        foreach (var group in Groups)
        {
            signedGroups[group] = Sum(months.Count, own.Where(kv => byId[kv.Key].Group == group).Select(kv => kv.Value));
        }

        var net = Combine(signedGroups[DreGroup.GrossRevenue], signedGroups[DreGroup.Deductions]);
        netRevenueTotal = net.Sum();
        foreach (var group in Groups)
        {
            groupRows[group] = AccountRows(group, months.Count, byId, own, netRevenueTotal);
        }

        var grossProfit = Combine(net, signedGroups[DreGroup.Costs]);
        var operating = Combine(grossProfit, signedGroups[DreGroup.OperatingExpenses]);
        var result = Combine(operating, signedGroups[DreGroup.FinancialResult], signedGroups[DreGroup.OtherResults]);

        var rows = new List<DreRow>();
        void AddGroup(DreGroup group)
        {
            var factor = DisplayFactor(group);
            rows.Add(Row(GroupKey(group), DreRowKind.Group, GroupLabel(group), group, null, null, null, 1, signedGroups[group].Select(v => v * factor).ToArray(), netRevenueTotal, false, groupRows[group].Count > 0));
            rows.AddRange(groupRows[group]);
        }

        AddGroup(DreGroup.GrossRevenue);
        AddGroup(DreGroup.Deductions);
        rows.Add(Row(NetRevenue, DreRowKind.Subtotal, "= Receita líquida", null, null, null, null, 1, net, netRevenueTotal, false, false));
        AddGroup(DreGroup.Costs);
        rows.Add(Row(GrossProfit, DreRowKind.Subtotal, "= Lucro bruto", null, null, null, null, 1, grossProfit, netRevenueTotal, true, false));
        AddGroup(DreGroup.OperatingExpenses);
        rows.Add(Row(OperatingResult, DreRowKind.Subtotal, "= Resultado operacional", null, null, null, null, 1, operating, netRevenueTotal, true, false));
        AddGroup(DreGroup.FinancialResult);
        AddGroup(DreGroup.OtherResults);
        rows.Add(Row(PeriodResult, DreRowKind.Subtotal, "= Resultado do período", null, null, null, null, 1, result, netRevenueTotal, true, false));

        var memo = advances is { Count: > 0 } && advances.Count == months.Count ? advances.ToArray() : new decimal[months.Count];
        var (delta, deltaPct) = Delta(memo);
        rows.Add(new DreRow(Advances, DreRowKind.Memo, "Adiantamentos de clientes (sinal ainda sem nota)", null, null, null, null, 1, memo,
            memo.Length == 0 ? 0 : memo[^1], null, delta, deltaPct, false, false));
        return new DreStatement(months, rows);
    }

    private static List<DreRow> AccountRows(DreGroup group, int count, Dictionary<Guid, DreAccount> byId, Dictionary<Guid, decimal[]> own, decimal netRevenueTotal)
    {
        // Pai dentro do grupo: o ancestral mais próximo que também está no grupo (1.3, filha de "1 Receitas", fica na raiz do Resultado financeiro).
        Guid? ParentInGroup(DreAccount account)
        {
            var parent = account.ParentId;
            var guard = 0;
            while (parent is { } p && byId.TryGetValue(p, out var candidate) && guard++ < 16)
            {
                if (candidate.Group == group)
                {
                    return candidate.Id;
                }

                parent = candidate.ParentId;
            }

            return null;
        }

        var withValues = own.Keys.Where(id => byId[id].Group == group && own[id].Any(v => v != 0)).ToHashSet();
        var visible = new HashSet<Guid>(withValues);
        foreach (var id in withValues)
        {
            for (var parent = ParentInGroup(byId[id]); parent is { } p && visible.Add(p); parent = ParentInGroup(byId[p]))
            {
            }
        }

        var children = visible.GroupBy(id => ParentInGroup(byId[id])).ToDictionary(g => g.Key ?? Guid.Empty, g => g.OrderBy(id => TreeCode.SortKey(byId[id].Code), StringComparer.Ordinal).ToList());
        var factor = DisplayFactor(group);
        var rows = new List<DreRow>();
        decimal[] Walk(Guid id, string parentKey, int level)
        {
            var account = byId[id];
            var total = own.TryGetValue(id, out var values) ? (decimal[])values.Clone() : new decimal[count];
            var position = rows.Count;
            rows.Add(null!);
            var kids = children.GetValueOrDefault(id) ?? [];
            foreach (var child in kids)
            {
                var sub = Walk(child, AccountKey(id), level + 1);
                for (var i = 0; i < count; i++)
                {
                    total[i] += sub[i];
                }
            }

            rows[position] = Row(AccountKey(id), DreRowKind.Account, account.Name, group, id, account.Code, parentKey, level, total.Select(v => v * factor).ToArray(), netRevenueTotal, false, kids.Count > 0);
            return total;
        }

        foreach (var root in children.GetValueOrDefault(Guid.Empty) ?? [])
        {
            Walk(root, GroupKey(group), 2);
        }

        return rows;
    }

    private static DreRow Row(string key, DreRowKind kind, string label, DreGroup? group, Guid? accountId, string? code, string? parentKey, int level, decimal[] values, decimal netRevenueTotal, bool critical, bool hasChildren)
    {
        var total = values.Sum();
        var (delta, deltaPct) = Delta(values);
        decimal? pct = netRevenueTotal == 0 ? null : Math.Round(total / netRevenueTotal * 100m, 2, MidpointRounding.AwayFromZero);
        return new DreRow(key, kind, label, group, accountId, code, parentKey, level, values, total, pct, delta, deltaPct, critical, hasChildren);
    }

    /// <summary>"Δ mês anterior": último mês − o anterior, em valor e em % sobre o anterior (nulo sem mês anterior ou com anterior zero).</summary>
    public static (decimal? Delta, decimal? Percent) Delta(IReadOnlyList<decimal> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2)
        {
            return (null, null);
        }

        var delta = values[^1] - values[^2];
        return (delta, values[^2] == 0 ? null : Math.Round(delta / Math.Abs(values[^2]) * 100m, 2, MidpointRounding.AwayFromZero));
    }

    private static decimal[] Sum(int count, IEnumerable<decimal[]> series)
    {
        var total = new decimal[count];
        foreach (var s in series)
        {
            for (var i = 0; i < count; i++)
            {
                total[i] += s[i];
            }
        }

        return total;
    }

    private static decimal[] Combine(params decimal[][] series) => Sum(series[0].Length, series);
}

/// <summary>
/// Regras de entrada no DRE por documento (FIN-08 CA 3 a 8, P-17 competência): o que entra, em que data e com que sinal. As
/// consultas do Finance usam estas mesmas listas, para a regra ter uma fonte só.
/// </summary>
public static class DreRules
{
    /// <summary>Títulos que não entram: cancelado e previsto (ainda não é documento).</summary>
    public static readonly ReceivableStatus[] ExcludedReceivableStatuses = [ReceivableStatus.Cancelled, ReceivableStatus.Planned];

    public static readonly PayableStatus[] ExcludedPayableStatuses = [PayableStatus.Cancelled, PayableStatus.Planned];

    /// <summary>Recebível entra na receita (CA 3): não cancelado, não previsto e fora da implantação (FIN-00, D2).</summary>
    public static bool IncludesReceivable(ReceivableStatus status, DocumentOrigin origin) => !ExcludedReceivableStatuses.Contains(status) && origin != DocumentOrigin.Opening;

    /// <summary>Título a pagar entra na despesa (CA 6), pelo valor bruto.</summary>
    public static bool IncludesPayable(PayableStatus status, DocumentOrigin origin) => !ExcludedPayableStatuses.Contains(status) && origin != DocumentOrigin.Opening;

    /// <summary>Data do título a pagar no DRE (P-17): a competência quando houver (recorrente), senão a data do documento.</summary>
    public static DateOnly PayableDate(DateOnly? competence, DateOnly issueDate) => competence ?? issueDate;

    /// <summary>Sinal (CA 5): parcela do contrato do marco com gatilho Assinatura (COM-01b).</summary>
    public static bool IsDownPayment(DocumentOrigin origin, TriggerEvent? trigger) => origin == DocumentOrigin.Contract && trigger == TriggerEvent.Signature;

    /// <summary>
    /// Baixa a receber (CA 8): juros e multa recebidos entram (+) na conta de receitas financeiras; o desconto concedido (inclusive
    /// "Quitar o saldo com desconto") sai (−) na conta de desconto concedido.
    /// </summary>
    public static IEnumerable<(Guid AccountId, decimal Value)> ReceivableSettlement(decimal interestAndFine, decimal discount, DreSettlementAccounts accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (interestAndFine != 0)
        {
            yield return (accounts.InterestIncome, interestAndFine);
        }

        if (discount != 0)
        {
            yield return (accounts.DiscountGranted, -discount);
        }
    }

    /// <summary>Baixa a pagar (CA 8): juros e multa pagos saem (−); o desconto obtido entra (+).</summary>
    public static IEnumerable<(Guid AccountId, decimal Value)> PayableSettlement(decimal interestAndFine, decimal discount, DreSettlementAccounts accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        if (interestAndFine != 0)
        {
            yield return (accounts.InterestExpense, -interestAndFine);
        }

        if (discount != 0)
        {
            yield return (accounts.DiscountReceived, discount);
        }
    }
}

/// <summary>Contas das baixas no DRE (CA 8), dos parâmetros <c>finance.dre_*_account_id</c>.</summary>
public sealed record DreSettlementAccounts(Guid InterestIncome, Guid InterestExpense, Guid DiscountGranted, Guid DiscountReceived);

/// <summary>
/// Quando o sinal vira receita no modo provisório (CA 5, D4): no primeiro romaneio expedido do projeto; sem romaneio, junto com o
/// título do marco seguinte. Até lá, fica em "Adiantamentos de clientes".
/// </summary>
public static class DownPaymentRule
{
    public static (DateOnly On, string Note)? Recognition(DateOnly? firstShippedOn, DateOnly? nextMilestoneIssueDate, string? nextMilestoneName)
    {
        if (firstShippedOn is { } shipped)
        {
            return (shipped, $"Sinal reconhecido na expedição de {shipped:dd/MM/yyyy}");
        }

        if (nextMilestoneIssueDate is { } next)
        {
            return (next, $"Sinal reconhecido com o marco {nextMilestoneName ?? "seguinte"}");
        }

        return null;
    }

    /// <summary>"CTR-2026-0001 — Fabricação concluída (40%)" → "Fabricação concluída (40%)".</summary>
    public static string MilestoneName(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        var dash = description.IndexOf(" — ", StringComparison.Ordinal);
        return dash < 0 ? description : description[(dash + 3)..];
    }

    /// <summary>Saldo dos adiantamentos no fim do mês: sinais emitidos até o fim do mês e ainda não reconhecidos nele.</summary>
    public static decimal BalanceAt(DateOnly monthEnd, IEnumerable<(DateOnly IssueDate, DateOnly? RecognizedOn, decimal Amount)> downPayments) =>
        (downPayments ?? []).Where(d => d.IssueDate <= monthEnd && (d.RecognizedOn is null || d.RecognizedOn > monthEnd)).Sum(d => d.Amount);
}

/// <summary>Marca da última consulta do DRE por usuário (<c>finance.dre_view_marks</c>, CA 9), fora da auditoria.</summary>
public sealed class DreViewMark : Entity
{
    public Guid UserId { get; init; }

    public DateTimeOffset ViewedAt { get; set; }
}
