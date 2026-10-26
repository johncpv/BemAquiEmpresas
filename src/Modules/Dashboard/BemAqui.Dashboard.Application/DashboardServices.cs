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
        // DSH-01b: cards de margem, projetos, carteira, produção e entregas e o resultado gerencial por projeto.
        services.TryAddScoped<DashboardAccess>();
        services.TryAddScoped<OwnerProjectsService>();
        services.TryAddScoped<ProjectResultsService>();
        services.TryAddScoped<ICashForecastSource, CashForecastSource>(); // FIN-07: ICashForecast
        services.TryAddScoped<IRevenueSource, RevenueSource>(); // FIN-08: IRevenueSummary
        services.TryAddScoped<IDecisionAgenda, DecisionAgenda>(); // DSH-04: IAgendaService (nível 1)
        // DSH-03: painel do supervisor (blocos pelos contratos do Production, do Inventory e do Purchasing).
        services.TryAddScoped<SupervisorService>();
        services.TryAddScoped<SupervisorShortages>();
        return services;
    }
}
