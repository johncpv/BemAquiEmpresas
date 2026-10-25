namespace BemAqui.Dashboard.Application;

// Portas para contratos de stories que ainda não estão na main (DSH-01a, Change Log): cada uma tem uma implementação
// "desligada", que devolve nulo, e o card mostra "ainda não disponível". O ponto de ligação de cada porta é um adaptador no
// Dashboard que chama o contrato do dono e troca o registro em DashboardServices.AddDashboard; nada muda nos cards.

/// <summary>Saldo projetado no fim de uma semana, nos dois cenários.</summary>
public sealed record CashForecastWeek(DateOnly WeekEnd, decimal BaseBalance, decimal AdjustedBalance);

/// <summary>Saldo final no horizonte de <see cref="Days"/> dias (30, 60 e 90), nos dois cenários.</summary>
public sealed record CashForecastHorizon(int Days, decimal BaseBalance, decimal AdjustedBalance);

/// <summary>Um dia e o saldo previsto nele (menor saldo, primeiro dia negativo).</summary>
public sealed record CashForecastPoint(DateOnly Date, decimal Balance);

/// <summary>Projeção base × ajustada das próximas semanas, como a FIN-07 devolve no <c>ICashForecast.SummaryAsync</c>.</summary>
public sealed record CashForecastSnapshot(
    decimal CurrentBalance, IReadOnlyList<CashForecastWeek> Weeks, IReadOnlyList<CashForecastHorizon> Horizons, CashForecastPoint LowestBase,
    CashForecastPoint LowestAdjusted, CashForecastPoint? FirstAdjustedNegative);

/// <summary>
/// Caixa projetado (FIN-07). Ligação: adaptador sobre <c>ICashForecast.SummaryAsync</c> (cenário ajustado) do
/// <c>Finance.Contracts</c>, com as 12 semanas e os horizontes de 30, 60 e 90 dias.
/// </summary>
public interface ICashForecastSource
{
    /// <summary>Nulo enquanto a projeção ajustada não está ligada.</summary>
    Task<CashForecastSnapshot?> SummaryAsync(int weeks, CancellationToken cancellationToken = default);
}

/// <summary>Receita bruta de um mês; <see cref="Provisional"/> quando vem dos títulos emitidos, antes das notas reais (D4).</summary>
public sealed record RevenueMonth(int Month, decimal Amount, bool Provisional);

public sealed record RevenueSnapshot(int Year, IReadOnlyList<RevenueMonth> Months);

/// <summary>
/// Receita bruta por mês (FIN-08). Ligação: adaptador sobre <c>IRevenueSummary</c> do <c>Finance.Contracts</c>, a mesma linha
/// "Receita bruta" do DRE, com a marca de provisório.
/// </summary>
public interface IRevenueSource
{
    /// <summary>Nulo enquanto o resumo do faturamento não está ligado.</summary>
    Task<RevenueSnapshot?> YearAsync(int year, CancellationToken cancellationToken = default);
}

/// <summary>Item do nível 1 da pauta: o que espera a decisão do usuário, com o link do documento.</summary>
public sealed record DecisionItem(string Kind, string Title, string? Detail, decimal? Amount, DateOnly? DueDate, string Url);

/// <summary>
/// "Aguardando sua decisão" (DSH-04). Ligação: adaptador sobre <c>IAgendaService</c>, só os itens do nível 1 do usuário, cada
/// um já filtrado pela permissão própria (OP, aditivos, qualidade, pedidos, ajustes).
/// </summary>
public interface IDecisionAgenda
{
    /// <summary>Nulo enquanto a pauta não está ligada.</summary>
    Task<IReadOnlyList<DecisionItem>?> PendingForAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Desligada até o merge da FIN-07.</summary>
public sealed class OffCashForecastSource : ICashForecastSource
{
    public Task<CashForecastSnapshot?> SummaryAsync(int weeks, CancellationToken cancellationToken = default) => Task.FromResult<CashForecastSnapshot?>(null);
}

/// <summary>Desligada até o merge da FIN-08.</summary>
public sealed class OffRevenueSource : IRevenueSource
{
    public Task<RevenueSnapshot?> YearAsync(int year, CancellationToken cancellationToken = default) => Task.FromResult<RevenueSnapshot?>(null);
}

/// <summary>Desligada até o merge da DSH-04.</summary>
public sealed class OffDecisionAgenda : IDecisionAgenda
{
    public Task<IReadOnlyList<DecisionItem>?> PendingForAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DecisionItem>?>(null);
}
