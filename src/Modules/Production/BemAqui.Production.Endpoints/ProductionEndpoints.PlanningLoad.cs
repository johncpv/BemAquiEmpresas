using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Production.Endpoints;

internal static partial class ProductionEndpoints
{
    /// <summary>
    /// Ocupação da fábrica (PLN-04 CA 1 a 12), no grupo do módulo <c>production</c>: a tabela e o drill-down da célula com
    /// <c>production.view</c>; filtros por allowlist (422 fora dela). Sem custo.
    /// </summary>
    private static void MapPlanningLoad(RouteGroupBuilder load)
    {
        load.MapGet(string.Empty, async (int? weeks, Guid[]? workCenterIds, string? line, FactoryLoadService service, CancellationToken ct) =>
                (await service.ViewAsync(new FactoryLoadQuery(weeks, workCenterIds, line), ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetFactoryLoad").Produces<FactoryLoadView>();
        load.MapGet("/cell", async (Guid? workCenterId, DateOnly weekStart, Guid[]? workCenterIds, string? line, FactoryLoadService service, CancellationToken ct) =>
                (await service.CellAsync(workCenterId, weekStart, new FactoryLoadQuery(null, workCenterIds, line), ct)).ToHttpResult())
            .RequirePermission("production.view").WithName("GetFactoryLoadCell").Produces<FactoryLoadCellDetail>();
    }
}
