using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Finance.Contracts;

namespace BemAqui.Dashboard.Application;

// Portas do painel sobre os contratos dos donos (DSH-01a): o caixa projetado da FIN-07, a receita bruta da FIN-08 e a pauta da
// DSH-04. Cada adaptador só traduz o contrato para o formato do card; nenhum número é recalculado aqui (ADR 0028 §1).

/// <summary>Saldo projetado no fim de uma semana, nos dois cenários.</summary>
public sealed record CashForecastWeek(DateOnly WeekEnd, decimal BaseBalance, decimal AdjustedBalance);

/// <summary>Saldo final no horizonte de <see cref="Days"/> dias (30, 60 e 90), nos dois cenários.</summary>
public sealed record CashForecastHorizon(int Days, decimal BaseBalance, decimal AdjustedBalance);

/// <summary>Um dia e o saldo previsto nele (menor saldo, primeiro dia negativo).</summary>
public sealed record CashForecastPoint(DateOnly Date, decimal Balance);

/// <summary>Projeção base × ajustada das próximas semanas, com os horizontes e a diferença causada pelos atrasos.</summary>
public sealed record CashForecastSnapshot(
    decimal CurrentBalance, IReadOnlyList<CashForecastWeek> Weeks, IReadOnlyList<CashForecastHorizon> Horizons, CashForecastPoint? LowestBase,
    CashForecastPoint? LowestAdjusted, decimal Difference, CashForecastPoint? FirstAdjustedNegative);

public interface ICashForecastSource
{
    Task<CashForecastSnapshot> SummaryAsync(int weeks, CancellationToken cancellationToken = default);
}

/// <summary>Receita bruta de um mês; <see cref="Provisional"/> quando vem dos títulos emitidos, antes das notas reais (D4).</summary>
public sealed record RevenueMonth(int Month, decimal Amount, bool Provisional);

public sealed record RevenueSnapshot(int Year, IReadOnlyList<RevenueMonth> Months);

public interface IRevenueSource
{
    /// <summary>Receita bruta de janeiro até o mês de <paramref name="until"/>.</summary>
    Task<RevenueSnapshot> YearToDateAsync(DateOnly until, CancellationToken cancellationToken = default);
}

/// <summary>Item do nível 1 da pauta: a categoria, quantos documentos esperam a decisão, o total e o link da ação.</summary>
public sealed record DecisionItem(string Kind, string Title, string? Detail, decimal? Amount, DateOnly? DueDate, string Url, int Count = 1);

public interface IDecisionAgenda
{
    /// <summary>O nível 1 ("destrava outra pessoa ou dinheiro") da pauta do usuário do request, na ordem da pauta.</summary>
    Task<IReadOnlyList<DecisionItem>> PendingAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Caixa projetado pela FIN-07: a série semanal da tela do fluxo (<see cref="ICashForecast.WeeksAsync"/>) e o saldo final em 30, 60
/// e 90 dias da mesma montagem, iguais ao <see cref="ICashForecast.SummaryAsync"/> de cada horizonte.
/// </summary>
public sealed class CashForecastSource(ICashForecast forecast) : ICashForecastSource
{
    public static readonly int[] HorizonDays = [30, 60, 90];

    public async Task<CashForecastSnapshot> SummaryAsync(int weeks, CancellationToken cancellationToken = default)
    {
        // Uma montagem só do fluxo (CA 14): a série das semanas e o saldo de 30, 60 e 90 dias saem dela.
        var series = await forecast.WeeksAsync(weeks * 7, HorizonDays, cancellationToken);
        var horizons = series.Horizons.Select(h => new CashForecastHorizon(h.Days, h.BaseBalance, h.AdjustedBalance)).ToList();
        static CashForecastPoint? Point(CashForecastDay? d) => d is null ? null : new CashForecastPoint(d.Date, d.Balance);
        return new CashForecastSnapshot(series.CurrentBalance, series.Weeks.Select(w => new CashForecastWeek(w.WeekEnd, w.BaseBalance, w.AdjustedBalance)).ToList(), horizons,
            Point(series.BaseLowest), Point(series.AdjustedLowest), series.LowestDifference, Point(series.AdjustedFirstNegative));
    }
}

/// <summary>Receita bruta pela FIN-08: a linha "Receita bruta" do DRE, mês a mês, com a marca de provisório.</summary>
public sealed class RevenueSource(IRevenueSummary revenue) : IRevenueSource
{
    public async Task<RevenueSnapshot> YearToDateAsync(DateOnly until, CancellationToken cancellationToken = default)
    {
        var months = await revenue.GrossRevenueAsync(new DateOnly(until.Year, 1, 1), new DateOnly(until.Year, until.Month, DateTime.DaysInMonth(until.Year, until.Month)), cancellationToken);
        return new RevenueSnapshot(until.Year, months.Where(m => m.Year == until.Year).Select(m => new RevenueMonth(m.Month, m.Amount, m.IsProvisional)).ToList());
    }
}

/// <summary>"Aguardando sua decisão" pela DSH-04: as categorias de nível 1 da Minha pauta, com as mesmas regras e a mesma ordem.</summary>
public sealed class DecisionAgenda(IAgendaService agenda) : IDecisionAgenda
{
    public async Task<IReadOnlyList<DecisionItem>> PendingAsync(CancellationToken cancellationToken = default)
    {
        var level1 = agenda.Registered.Where(c => c.Tier == AgendaTier.Unblocks).Select(c => c.Code).ToList();
        if (level1.Count == 0)
        {
            return [];
        }

        var view = await agenda.ForUserAsync(level1, cancellationToken);
        return view.Items.Where(i => i.Tier == AgendaTier.Unblocks && i.Count > 0)
            .Select(i => new DecisionItem(i.Category, i.Title, i.Why, i.Amount, i.DueDate, i.Link ?? (i.Entries.Count > 0 ? i.Entries[0].Link : "/app"), i.Count)).ToList();
    }
}

