using System.Globalization;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Cenário ajustado do fluxo de caixa (FIN-07, 04-fluxos §3.5): os recebíveis da base passam pelo
/// <see cref="AdjustedCashFlowCalculator"/> com a pontualidade e a inadimplência de todos os clientes numa consulta em lote
/// (CA 16); pagamentos, recorrentes e realizado ficam como na base. Nada é gravado. O resumo para o painel do dono
/// (<see cref="ICashForecast"/>) sai da mesma conta da tela (CA 14).
/// </summary>
public sealed partial class CashFlowService : ICashForecast
{
    public const string ScenarioBoth = "both";
    public const string ScenarioBase = "base";
    public const string ScenarioAdjusted = "adjusted";

    /// <summary>Cenários aceitos no fluxo; fora deles, 422.</summary>
    public static readonly IReadOnlyList<string> Scenarios = [ScenarioBoth, ScenarioBase, ScenarioAdjusted];

    /// <summary>Cenários aceitos no detalhe do dia.</summary>
    public static readonly IReadOnlyList<string> DayScenarios = [ScenarioBase, ScenarioAdjusted];

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public async Task<CashForecastSummary> SummaryAsync(int horizonDays, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(horizonDays, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(horizonDays, MaxDays);
        var today = BusinessTime.Today(clock);
        var flow = (await BuildAsync(new CashFlowQuery(CashFlowSeries.Daily, horizonDays, null, today, ScenarioBoth), cancellationToken)).Value;
        static CashForecastScenario Of(IReadOnlyList<CashFlowPeriod> periods, CashFlowPoint? lowest) =>
            new(periods.Count == 0 ? 0 : periods[^1].Balance, lowest?.Date, lowest?.Balance);
        var adjusted = flow.Adjusted!;
        return new CashForecastSummary(today, horizonDays, flow.CurrentBalance, Of(flow.Periods, flow.Lowest), Of(adjusted.Periods, adjusted.Lowest), adjusted.LowestDifference ?? 0);
    }

    /// <summary>Recebíveis projetados até <paramref name="to"/> (pelo saldo) passados pelo calculador, com o limite do parâmetro.</summary>
    private async Task<(AdjustedCashFlow Flow, IReadOnlyDictionary<Guid, CustomerDefaultStats> Stats, decimal Threshold)> CalculateAdjustedAsync(DateOnly today, DateOnly to, CancellationToken ct)
    {
        var rows = await db.Receivables.AsNoTracking().Where(r => OpenReceivable.Contains(r.Status) && r.DueDate != null && r.DueDate <= to)
            .Select(r => new { r.Id, r.PartyId, Due = r.DueDate!.Value, Balance = r.Amount - r.PaidAmount }).ToListAsync(ct);
        var stats = await paymentStats.ForPartiesAsync(rows.Select(r => r.PartyId).Distinct().ToList(), ct);
        var threshold = (await parameters.LimitsAsync(ct)).AdjustedDefaultThresholdPct;
        var any = stats.Values.FirstOrDefault();
        var punctuality = stats.ToDictionary(s => s.Key, s => new PartyPunctuality(s.Value.AvgDelayDays, s.Value.HasHistory, s.Value.DefaultPct));
        var flow = AdjustedCashFlowCalculator.Calculate(today, to, rows.Select(r => new ProjectedReceivable(r.Id, r.PartyId, r.Due, r.Balance)), punctuality,
            any?.TenantAvgDelayDays ?? 0, any?.TenantHasHistory ?? true, threshold);
        return (flow, stats, threshold);
    }

    private async Task<CashFlowAdjusted> AdjustedAsync(
        Dictionary<DateOnly, CashFlowDayTotals> totals, DateOnly from, DateOnly today, DateOnly to, decimal balance, string view, CashFlowPoint? baseLowest, CancellationToken ct)
    {
        var (flow, stats, threshold) = await CalculateAdjustedAsync(today, to, ct);
        var adjusted = new Dictionary<DateOnly, CashFlowDayTotals>(totals);
        foreach (var (day, delta) in flow.InflowDelta)
        {
            var t = adjusted.GetValueOrDefault(day);
            adjusted[day] = t with { Inflow = t.Inflow + delta };
        }

        // Os vencidos em aberto do ajustado são os que ainda caem hoje, pelo valor ponderado (CA 4).
        var t0 = adjusted.GetValueOrDefault(today);
        adjusted[today] = t0 with { OverdueInflow = flow.Receivables.Where(r => r.DueDate < today && r.AdjustedDate == today).Sum(r => r.AdjustedAmount) };
        var days = CashFlowSeries.Days(from, to, today, balance, adjusted);
        var (lowest, negative) = CashFlowSeries.Highlights(days);
        var risky = flow.Receivables.Where(r => r.Weighted && !r.BeyondHorizon).GroupBy(r => r.PartyId).ToList();
        var names = await NamesAsync(risky.Select(g => g.Key), ct);
        var atRisk = risky.Select(g => new CashFlowRiskCustomer(g.Key, names.GetValueOrDefault(g.Key), g.First().DefaultPct, g.Sum(r => r.AtRisk), g.Count()))
            .OrderByDescending(c => c.AtRisk).ThenBy(c => c.Name, StringComparer.CurrentCulture).ToList();
        var tenantAvg = stats.Values.FirstOrDefault()?.TenantAvgDelayDays ?? 0;
        return new CashFlowAdjusted(
            CashFlowSeries.Aggregate(days, view), lowest, negative, baseLowest is null || lowest is null ? null : baseLowest.Balance - lowest.Balance,
            new CashFlowShifted(flow.BeyondHorizonCount, flow.BeyondHorizonTotal), flow.AtRiskTotal, atRisk, flow.TenantHasHistory, tenantAvg, threshold);
    }

    /// <summary>
    /// Recebíveis do dia no ajustado (CA 9): os que caem nele, com o vencimento original, a data ajustada e o motivo, e os que
    /// saíram dele por deslocamento (fora do total). A pontualidade mostrada é só a do cliente de cada título (CA 17).
    /// </summary>
    private async Task AddAdjustedReceivablesAsync(List<CashFlowItem> items, DateOnly date, DateOnly today, CancellationToken ct)
    {
        var (flow, _, _) = await CalculateAdjustedAsync(today, date, ct);
        var landing = flow.Receivables.Where(r => r.AdjustedDate == date).ToList();
        var leaving = flow.Receivables.Where(r => r.BaseDate == date && r.AdjustedDate != date).ToList();
        var ids = landing.Concat(leaving).Select(r => r.Id).ToList();
        var docs = await db.Receivables.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.Number, r.Description }).ToDictionaryAsync(r => r.Id, ct);
        var names = await NamesAsync(landing.Concat(leaving).Select(r => r.PartyId), ct);
        CashFlowItem Item(AdjustedReceivable r, bool shifted)
        {
            var doc = docs[r.Id];
            var name = names.GetValueOrDefault(r.PartyId);
            var reason = Reason(r, name);
            return new CashFlowItem("Receivable", r.Id, doc.Number, doc.Description, name, shifted ? r.Balance : r.AdjustedAmount, r.DueDate, r.DueDate < today, false,
                "/app/financeiro/receber/" + r.Id, r.AdjustedDate, shifted ? $"Vai para {r.AdjustedDate.ToString("dd/MM/yyyy", PtBr)}. {reason}" : reason, shifted, r.Weighted ? r.Balance : null);
        }

        items.AddRange(landing.Select(r => Item(r, false)));
        items.AddRange(leaving.Select(r => Item(r, true)));
    }

    /// <summary>Motivo do deslocamento e da ponderação, em português (CA 9).</summary>
    public static string Reason(AdjustedReceivable r, string? partyName)
    {
        ArgumentNullException.ThrowIfNull(r);
        var days = r.DelayDays == 1 ? "1 dia" : $"{r.DelayDays} dias";
        var delay = r.Source switch
        {
            DelaySource.Customer => $"Atraso médio de {days} de {partyName ?? "cliente"}.",
            DelaySource.Tenant => $"Média da empresa: {days}.",
            _ => "Ainda não há histórico de pagamento: sem deslocamento.",
        };
        return r.Weighted ? $"{delay} Valor ponderado pela inadimplência de {r.DefaultPct.ToString("N2", PtBr)}%." : delay;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return list.Count == 0 ? [] : (await parties.FindManyAsync(list, ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
    }
}
