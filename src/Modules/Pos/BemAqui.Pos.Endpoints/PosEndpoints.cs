using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Pos.Endpoints;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StoreRequest(string Code, string Name, Guid StockLocationId, Guid DefaultPriceListId, bool IsActive = true);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegisterRequest(int Number, Guid CashAccountId, RegisterUiMode UiMode, bool IsActive = true);

/// <summary>
/// Lojas e caixas (PDV-01 CA 2 e 3): tabelas do <c>Pos</c>, telas de retaguarda autorizadas com <c>sales.*</c> (02, seção 4.1). A lista também
/// serve ao registro do aparelho (<c>pos.manage_devices</c>), que escolhe a loja e o caixa. O aperto de mão do aparelho fica em
/// <c>/api/v1/pos/sync/*</c>, só com o esquema <c>Device</c> (CA 9).
/// </summary>
internal static class PosEndpoints
{
    public static IEndpointRouteBuilder MapPosEndpoints(this IEndpointRouteBuilder api)
    {
        var stores = api.MapGroup("/pos/stores").WithTags("Lojas e caixas").RequireModule("sales");
        stores.MapGet(string.Empty, async (StoreService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequireAnyPermissionOf("sales.view", "pos.manage_devices").WithName("ListStores").Produces<IReadOnlyList<StoreView>>();
        stores.MapGet("/options", async (StoreService service, CancellationToken ct) => Results.Ok(await service.OptionsAsync(ct)))
            .RequireAnyPermission("sales.create", "sales.edit").WithName("GetStoreFormOptions").Produces<StoreFormOptions>();
        stores.MapPost(string.Empty, async (StoreRequest input, StoreService service, CancellationToken ct) =>
                (await service.CreateStoreAsync(ToInput(input), ct)).ToHttpResult(s => Results.Created("/api/v1/pos/stores/" + s.Id, s)))
            .RequirePermission("sales.create").WithName("CreateStore").Produces<StoreView>(StatusCodes.Status201Created);
        stores.MapPut("/{id:guid}", async (Guid id, StoreRequest input, StoreService service, CancellationToken ct) =>
                (await service.UpdateStoreAsync(id, ToInput(input), ct)).ToHttpResult())
            .RequirePermission("sales.edit").WithName("UpdateStore").Produces<StoreView>();
        stores.MapPost("/{id:guid}/registers", async (Guid id, RegisterRequest input, StoreService service, CancellationToken ct) =>
                (await service.CreateRegisterAsync(id, ToInput(input), ct)).ToHttpResult(s => Results.Created("/api/v1/pos/stores/" + s.Id, s)))
            .RequirePermission("sales.create").WithName("CreateRegister").Produces<StoreView>(StatusCodes.Status201Created);
        stores.MapPut("/{id:guid}/registers/{registerId:guid}", async (Guid id, Guid registerId, RegisterRequest input, StoreService service, CancellationToken ct) =>
                (await service.UpdateRegisterAsync(id, registerId, ToInput(input), ct)).ToHttpResult())
            .RequirePermission("sales.edit").WithName("UpdateRegister").Produces<StoreView>();

        // CA 8 a 10: só o principal do aparelho (esquema Device, lista fechada por teste). A loja e o caixa vêm do principal, nunca do corpo.
        var sync = api.MapGroup("/pos/sync").WithTags("PDV: sincronização").RequireModule("pos")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        sync.MapPost("/handshake", async (HttpContext http, PosSyncService service, CancellationToken ct) =>
            {
                var result = await service.HandshakeAsync(DevicePrincipal.From(http.User)!, ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                DeviceEndpoints.SetCookie(http, result.Value.Rotation.Secret, result.Value.Rotation.ExpiresAt);
                return Results.Ok(result.Value.Handshake);
            })
            .RequireDevice().WithName("PosHandshake").Produces<PosHandshake>();

        // OFF-01 CA 10 a 12: foto e delta do catálogo da loja do aparelho (a loja vem do principal; nada de outra loja, sem custo nem margem).
        sync.MapGet("/snapshot", async (HttpContext http, PosCatalogSyncService service, CancellationToken ct) =>
                (await service.SnapshotAsync(DevicePrincipal.From(http.User)!, ct)).ToHttpResult())
            .RequireDevice().WithName("GetPosCatalogSnapshot").Produces<PosCatalogSnapshot>();
        sync.MapGet("/changes", async (long cursor, int? limit, HttpContext http, PosCatalogSyncService service, CancellationToken ct) =>
                (await service.ChangesAsync(DevicePrincipal.From(http.User)!, cursor, limit, ct)).ToHttpResult())
            .RequireDevice().WithName("GetPosCatalogChanges").Produces<PosCatalogChanges>();

        // PDV-02 CA 15: o operador (sessão por PIN do caixa) acha o cliente cadastrado para ligar à venda; só nome e documento mascarado.
        var customers = api.MapGroup("/pos/customers").WithTags("PDV: venda").RequireModule("pos");
        customers.MapGet(string.Empty, async (string? term, PosCustomerSearchService service, CancellationToken ct) => Results.Ok(await service.SearchAsync(term, ct)))
            .RequirePermission("pos.create").AllowPosPinSession().WithName("SearchPosCustomers").Produces<IReadOnlyList<PosCustomer>>();
        return api;
    }

    private static StoreInput ToInput(StoreRequest r) => new(r.Code, r.Name, r.StockLocationId, r.DefaultPriceListId, r.IsActive);

    private static RegisterInput ToInput(RegisterRequest r) => new(r.Number, r.CashAccountId, r.UiMode, r.IsActive);
}
