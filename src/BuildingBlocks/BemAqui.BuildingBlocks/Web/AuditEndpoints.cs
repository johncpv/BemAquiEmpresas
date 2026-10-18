using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.BuildingBlocks.Web;

public static class AuditEndpoints
{
    /// <summary><c>GET /api/v1/audit/{entity}/{id}</c>: histórico paginado de um registro.</summary>
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/audit/{entity}/{id}", async (string entity, string id, int? page, int? pageSize, AuditHistory history, CancellationToken ct) =>
                (await history.GetAsync(entity, id, new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize), ct)).ToHttpResult())
            .WithName("GetAuditHistory").Produces<BemAqui.BuildingBlocks.Paging.PagedList<AuditEntryDto>>()
            .WithTags("Auditoria");
        return api;
    }
}
