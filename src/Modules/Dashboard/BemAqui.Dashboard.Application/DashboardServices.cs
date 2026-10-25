using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Dashboard.Application;

public static class DashboardServices
{
    /// <summary>
    /// Painel do proprietário (DSH-01a): serviço, cache em memória (singleton) e as portas desligadas. Ligar uma porta é trocar
    /// o registro dela pelo adaptador do contrato do dono (ver o Change Log da story).
    /// </summary>
    public static IServiceCollection AddDashboard(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<DashboardCache>();
        services.TryAddScoped<DashboardService>();
        services.TryAddScoped<ICashForecastSource, OffCashForecastSource>(); // FIN-07: ICashForecast.SummaryAsync
        services.TryAddScoped<IRevenueSource, OffRevenueSource>(); // FIN-08: IRevenueSummary
        services.TryAddScoped<IDecisionAgenda, OffDecisionAgenda>(); // DSH-04: IAgendaService
        return services;
    }
}
