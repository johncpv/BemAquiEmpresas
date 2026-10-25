using BemAqui.BuildingBlocks.Web;
using BemAqui.Finance.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Finance.Endpoints;

/// <summary>
/// DRE gerencial (FIN-08): leitura com <c>finance.view</c> (o grupo <c>/finance</c> já tem o <c>RequireModule</c>); os parâmetros
/// do DRE se leem com <c>finance.view</c> e se editam e conferem com <c>settings.edit</c>, conferida no serviço (ADR 0042 §6).
/// </summary>
internal static partial class FinanceEndpoints
{
    private static void MapDre(RouteGroupBuilder dre)
    {
        dre.MapGet(string.Empty, async (DateOnly? from, DateOnly? to, DreService service, CancellationToken ct) => (await service.BuildAsync(new DreQuery(from, to), ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetDre").Produces<DreReport>();
        dre.MapGet("/cells", async (Guid? accountId, string? group, DateOnly? month, DateOnly? from, DateOnly? to, string? sort, DreService service, CancellationToken ct) =>
                (await service.CellAsync(new DreCellQuery(accountId, group, month, from, to, sort), ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetDreCell").Produces<DreCell>();
        dre.MapGet("/changes", async (DateTimeOffset? since, DateOnly? from, DateOnly? to, DreService service, CancellationToken ct) =>
                (since is { } s ? await service.ChangesSinceAsync(s, from, to, ct) : await service.ChangesAsync(from, to, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("GetDreChanges").Produces<DreChanges>();
        dre.MapPost("/view-mark", async (DreService service, CancellationToken ct) =>
            {
                await service.MarkViewedAsync(ct);
                return Results.NoContent();
            })
            .RequirePermission("finance.view").WithName("MarkDreViewed").Produces(StatusCodes.Status204NoContent);
    }

    private static void MapDreParameters(RouteGroupBuilder parameters)
    {
        parameters.MapGet(string.Empty, async (DreParametersService service, CancellationToken ct) => Results.Ok(await service.GetAsync(ct)))
            .RequirePermission("finance.view").WithName("GetDreParameters").Produces<DreParametersView>();
        parameters.MapPut(string.Empty, async (DreParametersInput input, DreParametersService service, CancellationToken ct) => (await service.UpdateAsync(input, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("UpdateDreParameters").Produces<DreParametersView>();
        parameters.MapPost("/confirm", async (DreParameterConfirmInput input, DreParametersService service, CancellationToken ct) => (await service.ConfirmAsync(input.Key, ct)).ToHttpResult())
            .RequirePermission("finance.view").WithName("ConfirmDreParameter").Produces<DreParametersView>();
    }
}
