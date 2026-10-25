using BemAqui.BuildingBlocks.Web;
using BemAqui.Dashboard.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Dashboard.Endpoints;

/// <summary>
/// Painel do proprietário (DSH-01a CA 4, 11 e 16): o grupo exige o módulo <c>dashboard</c> liberado e <c>dashboard.view</c>; cada
/// card exige também o módulo e a permissão do dono do dado (ADR 0028 §2). Respostas com <c>no-store</c>: trazem saldos.
/// </summary>
internal static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder api)
    {
        var dashboard = api.MapGroup("/dashboard").WithTags("Painel do proprietário").RequireModule("dashboard").RequirePermission("dashboard.view")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        dashboard.MapGet(string.Empty, async (DashboardService service, CancellationToken ct) => Results.Ok(await service.OverviewAsync(ct)))
            .WithName("GetDashboardOverview").Produces<DashboardOverview>();
        dashboard.MapPost("/refresh", (DashboardService service) =>
            {
                service.Refresh();
                return Results.NoContent();
            })
            .RequireRateLimiting(DashboardRefreshPolicy.Name).WithName("RefreshDashboard").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status429TooManyRequests);

        var cards = dashboard.MapGroup("/cards");
        cards.MapGet("/balances", async (DashboardService service, CancellationToken ct) => Results.Ok(await service.BalancesAsync(ct)))
            .RequireModule("finance").RequirePermission("finance.view").WithName("GetDashboardBalances").Produces<BalancesCard>();
        cards.MapGet("/cash-forecast", async (DashboardService service, CancellationToken ct) => Results.Ok(await service.CashForecastAsync(ct)))
            .RequireModule("finance").RequirePermission("finance.view").WithName("GetDashboardCashForecast").Produces<CashForecastCard>();
        cards.MapGet("/revenue", async (DashboardService service, CancellationToken ct) => Results.Ok(await service.RevenueAsync(ct)))
            .RequireModule("finance").RequirePermission("finance.view").WithName("GetDashboardRevenue").Produces<RevenueCard>();
        cards.MapGet("/overdue", async (DashboardService service, CancellationToken ct) => Results.Ok(await service.OverdueAsync(ct)))
            .RequireModule("finance").RequirePermission("finance.view").WithName("GetDashboardOverdue").Produces<OverdueCard>();
        cards.MapGet("/commercial", async (DashboardService service, CancellationToken ct) => Results.Ok(await service.CommercialAsync(ct)))
            .RequireModule("quotes").RequirePermission("quotes.view").WithName("GetDashboardCommercial").Produces<CommercialCard>();
        // Cada item da pauta já vem filtrado pela permissão própria (DSH-04): o card só exige o painel.
        cards.MapGet("/decisions", async (DashboardService service, CancellationToken ct) => Results.Ok(await service.DecisionsAsync(ct)))
            .WithName("GetDashboardDecisions").Produces<DecisionsCard>();
        return api;
    }
}
