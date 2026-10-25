namespace BemAqui.Finance.Domain;

/// <summary>Recebível projetado (aberto, parcial ou previsto com data) pelo saldo, como a base da FIN-05 o lança.</summary>
public sealed record ProjectedReceivable(Guid Id, Guid PartyId, DateOnly DueDate, decimal Balance);

/// <summary>Pontualidade e inadimplência do cliente que o ajustado usa (FIN-07 CA 2 e 3).</summary>
public sealed record PartyPunctuality(decimal AvgDelayDays, bool HasHistory, decimal DefaultPct);

/// <summary>De onde vem o atraso usado no deslocamento.</summary>
public enum DelaySource
{
    /// <summary>Atraso médio do próprio cliente (últimos 12 meses).</summary>
    Customer,

    /// <summary>Cliente sem histórico: média da empresa.</summary>
    Tenant,

    /// <summary>A empresa ainda não tem histórico: sem deslocamento.</summary>
    None,
}

/// <summary>
/// Recebível no ajustado: <see cref="BaseDate"/> é o dia em que a base o lança (vencidos entram hoje); <see cref="AdjustedDate"/> o
/// dia em que o ajustado o lança, pelo <see cref="AdjustedAmount"/>; <see cref="BeyondHorizon"/> quando saiu do período.
/// </summary>
public sealed record AdjustedReceivable(
    Guid Id,
    Guid PartyId,
    DateOnly DueDate,
    DateOnly BaseDate,
    DateOnly AdjustedDate,
    decimal Balance,
    decimal AdjustedAmount,
    int DelayDays,
    DelaySource Source,
    decimal DefaultPct,
    bool Weighted,
    bool BeyondHorizon)
{
    /// <summary>Parte que provavelmente não entra (CA 3): saldo − valor ponderado.</summary>
    public decimal AtRisk => Balance - AdjustedAmount;
}

/// <summary>Resultado do ajustado: cada recebível e a variação de entrada por dia em relação à base.</summary>
public sealed record AdjustedCashFlow(IReadOnlyList<AdjustedReceivable> Receivables, IReadOnlyDictionary<DateOnly, decimal> InflowDelta, bool TenantHasHistory)
{
    /// <summary>
    /// Saldo dos recebíveis empurrados para depois do horizonte (CA 5). Com <see cref="AtRiskTotal"/>, fecha a conta:
    /// entradas da base − entradas do ajustado = risco no período + deslocado para depois.
    /// </summary>
    public decimal BeyondHorizonTotal => Receivables.Where(r => r.BeyondHorizon && r.BaseDate <= Horizon).Sum(r => r.Balance);

    public int BeyondHorizonCount => Receivables.Count(r => r.BeyondHorizon && r.BaseDate <= Horizon);

    /// <summary>Risco de não receber no período (CA 3): a parte ponderada dos recebíveis que ficam no horizonte.</summary>
    public decimal AtRiskTotal => Receivables.Where(r => !r.BeyondHorizon).Sum(r => r.AtRisk);

    /// <summary>Último dia do período.</summary>
    public DateOnly Horizon { get; init; } = DateOnly.MaxValue;
}

/// <summary>
/// Cenário ajustado do fluxo de caixa (FIN-07, 04-fluxos §3.5), puro e sem banco. Cada recebível entra pelo saldo na data
/// max(hoje, vencimento + atraso médio do cliente), com o atraso arredondado a dias inteiros (<c>AwayFromZero</c>) e em dias
/// corridos; cliente sem histórico usa a média da empresa. Cliente com inadimplência acima do limite entra com o valor ×
/// (1 − inadimplência ÷ 100), arredondado a 2 casas por título. Pagamentos, recorrentes e realizado não mudam.
/// </summary>
public static class AdjustedCashFlowCalculator
{
    /// <summary>Dias de atraso a partir dos quais o vencido conta como inadimplência (ponto 1 da revisão).</summary>
    public const int DefaultOverdueDays = 30;

    /// <summary>
    /// Inadimplência do cliente (CA 3) = min(100, vencido há mais de 30 dias ÷ faturado em 12 meses × 100), com 2 casas;
    /// faturado 0 dá 0% sem vencido e 100% com vencido.
    /// </summary>
    public static decimal DefaultPct(decimal overdue30Amount, decimal last12mInvoiced)
    {
        if (overdue30Amount <= 0)
        {
            return 0m;
        }

        if (last12mInvoiced <= 0)
        {
            return 100m;
        }

        return Math.Min(100m, Math.Round(overdue30Amount / last12mInvoiced * 100m, 2, MidpointRounding.AwayFromZero));
    }

    /// <summary>Atraso em dias inteiros e a origem dele: do cliente, da empresa ou nenhum (CA 2).</summary>
    public static (int Days, DelaySource Source) Delay(PartyPunctuality? party, decimal tenantAvgDelayDays, bool tenantHasHistory)
    {
        if (party is { HasHistory: true })
        {
            return (ToDays(party.AvgDelayDays), DelaySource.Customer);
        }

        return tenantHasHistory ? (ToDays(tenantAvgDelayDays), DelaySource.Tenant) : (0, DelaySource.None);
    }

    /// <summary>Valor no ajustado: cheio no limite ou abaixo dele; acima, ponderado e arredondado por título (CA 3).</summary>
    public static decimal Weigh(decimal balance, decimal defaultPct, decimal thresholdPct) =>
        defaultPct > thresholdPct ? Math.Round(balance * (1m - (defaultPct / 100m)), 2, MidpointRounding.AwayFromZero) : balance;

    public static AdjustedCashFlow Calculate(
        DateOnly today,
        DateOnly to,
        IEnumerable<ProjectedReceivable> receivables,
        IReadOnlyDictionary<Guid, PartyPunctuality> parties,
        decimal tenantAvgDelayDays,
        bool tenantHasHistory,
        decimal thresholdPct)
    {
        ArgumentNullException.ThrowIfNull(receivables);
        ArgumentNullException.ThrowIfNull(parties);
        var result = new List<AdjustedReceivable>();
        var delta = new Dictionary<DateOnly, decimal>();
        void Add(DateOnly day, decimal amount) => delta[day] = delta.GetValueOrDefault(day) + amount;

        foreach (var r in receivables)
        {
            var party = parties.GetValueOrDefault(r.PartyId);
            var (days, source) = Delay(party, tenantAvgDelayDays, tenantHasHistory);
            var pct = party?.DefaultPct ?? 0m;
            var amount = Weigh(r.Balance, pct, thresholdPct);
            var baseDate = Max(today, r.DueDate);
            var adjusted = Max(today, r.DueDate.AddDays(days));
            var beyond = adjusted > to;
            result.Add(new AdjustedReceivable(r.Id, r.PartyId, r.DueDate, baseDate, adjusted, r.Balance, amount, days, source, pct, amount != r.Balance, beyond));
            if (baseDate <= to)
            {
                Add(baseDate, -r.Balance);
            }

            if (!beyond)
            {
                Add(adjusted, amount);
            }
        }

        return new AdjustedCashFlow(result, delta, tenantHasHistory) { Horizon = to };
    }

    private static int ToDays(decimal avg) => (int)Math.Round(Math.Max(0m, avg), 0, MidpointRounding.AwayFromZero);

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}
