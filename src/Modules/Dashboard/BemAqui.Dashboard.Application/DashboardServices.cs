using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Dashboard.Application;

public static class DashboardServices
{
    /// <summary>
    /// Painel do proprietário (DSH-01a): serviço, cache em memória (singleton) e os adaptadores dos contratos dos donos (FIN-07, FIN-08 e DSH-04).
    /// </summary>
    public static IServiceCollection AddDashboard(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DashboardCache>();
        services.TryAddScoped<DashboardService>();
        services.TryAddScoped<ICashForecastSource, CashForecastSource>(); // FIN-07: ICashForecast
        services.TryAddScoped<IRevenueSource, RevenueSource>(); // FIN-08: IRevenueSummary
        services.TryAddScoped<IDecisionAgenda, DecisionAgenda>(); // DSH-04: IAgendaService (nível 1)
        return services;
    }
}
