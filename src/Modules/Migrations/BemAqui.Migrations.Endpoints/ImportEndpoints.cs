using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Migrations.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Migrations.Endpoints;

/// <summary>
/// Tela só de leitura da importação (D4, CA 19): execuções, totais e pendências. Nenhuma rota de escrita: a carga é só pela
/// CLI (CA 8). Respostas com <c>no-store</c>, porque as pendências citam registros de terceiros.
/// </summary>
internal static class ImportEndpoints
{
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder api)
    {
        var runs = api.MapGroup("/settings/import-runs").WithTags("Configurações").RequireModule("settings")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        runs.MapGet(string.Empty, async (int? page, int? pageSize, ImportQueries queries, CancellationToken ct) =>
                Results.Ok(await queries.ListRunsAsync(new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)))
            .RequirePermission("settings.view").WithName("ListImportRuns").Produces<PagedList<ImportRunRow>>();
        runs.MapGet("/{id:guid}", async (Guid id, ImportQueries queries, CancellationToken ct) => (await queries.GetRunAsync(id, ct)).ToHttpResult())
            .RequirePermission("settings.view").WithName("GetImportRun").Produces<ImportRunDetail>();
        runs.MapGet("/{id:guid}/pending", async (Guid id, string? entity, string? kind, int? page, int? pageSize, ImportQueries queries, CancellationToken ct) =>
                (await queries.PendingAsync(id, new ImportPendingQuery(entity, kind), new PageRequest(page ?? 1, pageSize ?? PageRequest.DefaultPageSize).Normalize(), ct)).ToHttpResult())
            .RequirePermission("settings.view").WithName("ListImportPending").Produces<PagedList<ImportPendingItem>>();
        return api;
    }
}
