using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

internal static partial class ProductionEndpoints
{
    /// <summary>
    /// Cronograma (PLN-03 CA 1 a 14), no grupo do módulo <c>production</c>: por projeto e por centro com <c>production.view</c>; o de um
    /// projeto (aba do Projeto 360) com <c>production.view</c> e <c>projects.view</c>, e o projeto validado pelo <c>IProjects</c> (404). Filtros
    /// por allowlist (422 fora dela). Só leitura, sem custo.
    /// </summary>
    private static void MapPlanningGantt(RouteGroupBuilder planning)
    {
        planning.MapGet("/gantt/projects", async (DateOnly? from, DateOnly? to, Guid? projectId, Guid? partyId, string? line, Guid? workCenterId, bool? atRisk, GanttService service, CancellationToken ct) =>
                (await service.ProjectsAsync(new GanttQuery(from, to, projectId, partyId, line, workCenterId, atRisk == true), ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetProjectsGantt").Produces<GanttProjectsView>();
        planning.MapGet("/gantt/centers", async (DateOnly? from, DateOnly? to, string? line, Guid? workCenterId, GanttService service, CancellationToken ct) =>
                (await service.CentersAsync(new GanttQuery(from, to, Line: line, WorkCenterId: workCenterId), ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetCentersGantt").Produces<GanttCentersView>();
        planning.MapGet("/projects/{projectId:guid}/gantt", async (Guid projectId, DateOnly? from, DateOnly? to, GanttService service, CancellationToken ct) =>
                (await service.ProjectAsync(projectId, from, to, ct)).ToHttpResult())
            .RequirePermission("production.view").RequirePermission("projects.view").WithName("GetProjectGantt").Produces<GanttProjectsView>();
    }
}
