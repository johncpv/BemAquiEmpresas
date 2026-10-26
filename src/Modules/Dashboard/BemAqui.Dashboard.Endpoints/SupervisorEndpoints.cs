using BemAqui.BuildingBlocks.Web;
using BemAqui.Dashboard.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Dashboard.Endpoints;

/// <summary>
/// Painel do supervisor de produção (DSH-03 CA 1 e 9): a tela é da Fábrica, então o grupo exige o módulo <c>production</c> liberado e
/// <c>production.view</c> (não o <c>dashboard</c>: os endpoints moram no módulo de composição só como código, ADR 0028). Cada bloco confere o
/// módulo e a permissão do dono: as faltas num grupo próprio com o <c>inventory</c> e <c>inventory.view</c>. <c>?line=</c> filtra todos os blocos.
/// </summary>
internal static class SupervisorEndpoints
{
    public static IEndpointRouteBuilder MapSupervisorEndpoints(this IEndpointRouteBuilder api)
    {
        var supervisor = api.MapGroup("/dashboard/supervisor").WithTags("Painel do supervisor")
            .RequireModule(SupervisorBlocks.Module).RequirePermission(SupervisorBlocks.ViewPermission)
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        supervisor.MapGet(string.Empty, async (SupervisorService service, CancellationToken ct) => Results.Ok(await service.OverviewAsync(ct)))
            .WithName("GetSupervisorOverview").Produces<SupervisorOverview>();
        supervisor.MapGet("/board", async (string? line, SupervisorService service, CancellationToken ct) => Results.Ok(await service.BoardAsync(line, ct)))
            .WithName("GetSupervisorBoard").Produces<SupervisorBoardBlock>();
        supervisor.MapGet("/capacity", async (string? line, SupervisorService service, CancellationToken ct) => Results.Ok(await service.CapacityAsync(line, ct)))
            .WithName("GetSupervisorCapacity").Produces<SupervisorCapacityBlock>();
        // A lista de etapas para decidir só com production.approve_quality (o serviço confere); sem ela, a contagem e as NC.
        supervisor.MapGet("/quality", async (string? line, SupervisorService service, CancellationToken ct) => Results.Ok(await service.QualityAsync(line, ct)))
            .WithName("GetSupervisorQuality").Produces<SupervisorQualityBlock>();
        supervisor.MapGet("/indicators", async (string? line, SupervisorService service, CancellationToken ct) => Results.Ok(await service.IndicatorsAsync(line, ct)))
            .WithName("GetSupervisorIndicators").Produces<SupervisorIndicatorsBlock>();
        supervisor.MapPost("/refresh", (SupervisorService service) =>
            {
                service.Refresh();
                return Results.NoContent();
            })
            .RequireRateLimiting(DashboardRefreshPolicy.Name).WithName("RefreshSupervisorPanel").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status429TooManyRequests);

        var shortages = supervisor.MapGroup("/shortages").RequireModule(SupervisorBlocks.InventoryModule).RequirePermission(SupervisorBlocks.ShortagesPermission);
        shortages.MapGet(string.Empty, async (string? line, SupervisorShortages service, CancellationToken ct) => Results.Ok(await service.ListAsync(line, ct)))
            .WithName("GetSupervisorShortages").Produces<SupervisorShortagesBlock>();
        return api;
    }
}
