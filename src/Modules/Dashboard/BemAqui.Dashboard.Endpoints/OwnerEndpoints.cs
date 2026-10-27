using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Dashboard.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Dashboard.Endpoints;

/// <summary>
/// Cards da DSH-01b e o resultado gerencial por projeto (CA 1, 6 e 11; ADR 0028 §2): dentro do grupo do painel (módulo <c>dashboard</c> e
/// <c>dashboard.view</c>), cada card num grupo com um <c>RequireModule</c> e a permissão do dono do dado. Custo e margem só com
/// <c>projects.view_cost</c>; o recebido só com <c>finance.view</c>; o resultado por projeto com <c>projects.view_cost</c> e <c>finance.view</c>.
/// </summary>
internal static class OwnerEndpoints
{
    public static RouteGroupBuilder MapOwnerCards(this RouteGroupBuilder cards)
    {
        var margin = cards.MapGroup("/margin").RequireModule("projects").RequirePermission(DashboardCards.CostPermission);
        margin.MapGet(string.Empty, async (OwnerProjectsService service, CancellationToken ct) => (await service.MarginAsync(ct)).ToHttpResult())
            .WithName("GetDashboardMargin").Produces<MarginOverview>();

        var active = cards.MapGroup("/active-projects").RequireModule("projects").RequirePermission("projects.view");
        active.MapGet(string.Empty, async (OwnerProjectsService service, CancellationToken ct) => Results.Ok(await service.ActiveProjectsAsync(ct)))
            .WithName("GetDashboardActiveProjects").Produces<ActiveProjectsCard>();
        // A coluna de margem fica num grupo próprio: o DTO dela leva custo (ADR 0028 §2).
        var activeMargins = active.MapGroup("/margins").RequirePermission(DashboardCards.CostPermission);
        activeMargins.MapGet(string.Empty, async (OwnerProjectsService service, CancellationToken ct) => (await service.ActiveProjectMarginsAsync(ct)).ToHttpResult())
            .WithName("ListDashboardActiveProjectMargins").Produces<IReadOnlyList<ActiveProjectMarginRow>>();
        var activeReceived = active.MapGroup("/received").RequireModule("finance").RequirePermission("finance.view");
        activeReceived.MapGet(string.Empty, async (OwnerProjectsService service, CancellationToken ct) => Results.Ok(await service.ActiveProjectsReceivedAsync(ct)))
            .WithName("ListDashboardActiveProjectsReceived").Produces<IReadOnlyList<ActiveProjectReceivedItem>>();

        var backlog = cards.MapGroup("/backlog").RequireModule("projects").RequirePermission("projects.view");
        backlog.MapGet(string.Empty, async (OwnerProjectsService service, CancellationToken ct) => Results.Ok(await service.BacklogAsync(ct)))
            .WithName("GetDashboardBacklog").Produces<BacklogCard>();

        var deliveries = cards.MapGroup("/deliveries").RequireModule("projects").RequirePermission("projects.view");
        deliveries.MapGet(string.Empty, async (OwnerProjectsService service, CancellationToken ct) => Results.Ok(await service.DeliveriesAsync(ct)))
            .WithName("GetDashboardDeliveries").Produces<DeliveriesCard>();

        var production = cards.MapGroup("/production").RequireModule("production").RequirePermission("production.view");
        production.MapGet(string.Empty, async (OwnerProjectsService service, CancellationToken ct) => Results.Ok(await service.ProductionAsync(ct)))
            .WithName("GetDashboardProduction").Produces<ProductionCard>();
        return cards;
    }

    public static RouteGroupBuilder MapProjectResults(this RouteGroupBuilder dashboard)
    {
        var cost = dashboard.MapGroup("/project-results").RequireModule("projects").RequirePermission(DashboardCards.CostPermission);
        var results = cost.MapGroup(string.Empty).RequireModule("finance").RequirePermission("finance.view");
        results.MapGet(string.Empty, async (
                    string? situation, Guid? partyId, string? from, string? to, string? sort, int? page, int? pageSize, ProjectResultsService service, CancellationToken ct) =>
                (await service.ListAsync(situation, partyId, from, to, sort, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .WithName("ListDashboardProjectResults").Produces<ProjectResultsPage>().ProducesValidationProblem();
        results.MapGet("/{projectId:guid}", async (Guid projectId, string? from, string? to, ProjectResultsService service, CancellationToken ct) =>
                (await service.DetailAsync(projectId, from, to, ct)).ToHttpResult())
            .WithName("GetDashboardProjectResult").Produces<ProjectResultDetail>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound);
        results.MapGet("/{projectId:guid}/cell", async (Guid projectId, string? line, string? month, string? from, string? to, ProjectResultsService service, CancellationToken ct) =>
                (await service.CellAsync(projectId, line, month, from, to, ct)).ToHttpResult())
            .WithName("GetDashboardProjectResultCell").Produces<ProjectResultCell>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound);
        return dashboard;
    }
}
