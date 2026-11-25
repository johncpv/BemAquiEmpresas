using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using BemAqui.Identity.Application.Shopfloor;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

/// <summary>Entrada por PIN no caixa aceita: access token de 15 min e o fim absoluto da sessão (12 h, sem renovação além disso).</summary>
public sealed record PosPinSessionResponse(string AccessToken, DateTimeOffset ExpiresAt, DateTimeOffset SessionExpiresAt);

/// <summary>
/// PIN no aparelho de caixa (PDV-01 CA 11, ADR 0048) e usuário de operação (CA 12, ADR 0049). O Identity é dono do aparelho e do PIN; as rotas do
/// caixa ficam em <c>/api/v1/pos/…</c> e no módulo <c>pos</c>:
/// <list type="bullet">
/// <item>a lista de nomes e a entrada por PIN são do aparelho (esquema <c>Device</c>: cookie <c>__Host-bq_pos</c> e assinatura da chave do aparelho),
/// em <c>/api/v1/pos/sync/*</c>; não há rota anônima nova;</item>
/// <item>"quem está no caixa" e "sair" são da sessão por PIN do caixa, na lista <c>[PosPinSessionAllowed]</c>;</item>
/// <item>o "PIN do caixa" da própria pessoa exige login completo e <c>pos.create</c>;</item>
/// <item>o usuário de operação é criado, mudado e recebe PIN novo por quem tem <c>identity.create</c>, só no perfil Comércio.</item>
/// </list>
/// </summary>
internal static class PosPinEndpoints
{
    public static IEndpointRouteBuilder MapPosPinEndpoints(this IEndpointRouteBuilder api)
    {
        var sync = api.MapGroup("/pos/sync").WithTags("PDV: sincronização").RequireModule("pos")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        sync.MapGet("/operators", async (HttpContext http, PosPinService service, CancellationToken ct) =>
                (await service.OperatorsAsync(DevicePrincipal.From(http.User)!, ct)).ToHttpResult())
            .RequireDevice().WithName("ListPosOperators").Produces<PosOperators>();
        sync.MapPost("/pin", async (PosPinLoginInput input, HttpContext http, PosPinService service, CancellationToken ct) =>
                (await service.LoginAsync(DevicePrincipal.From(http.User)!, input, http.Request.Host.Host, AuthEndpoints.Ip(http), ct)).ToHttpResult(session =>
                {
                    AuthEndpoints.SetSession(http, session);
                    return Results.Ok(new PosPinSessionResponse(session.AccessToken, session.AccessTokenExpiresAt, session.SessionExpiresAt ?? session.AccessTokenExpiresAt));
                }))
            .RequireDevice().RequireRateLimiting(PinSessionEndpoints.PosPinLoginRateLimitPolicy).WithName("LoginPosPin").Produces<PosPinSessionResponse>();

        // Sessão por PIN do caixa: só estas rotas a alcançam (PosPinSessionAllowListTests). Sessão completa recebe 404 (ninguém entrou no caixa).
        var session = api.MapGroup("/pos/session").WithTags("PDV: sessão do operador").RequireModule("pos");
        session.MapGet(string.Empty, async (HttpContext http, ICurrentUser user, PosPinService service, CancellationToken ct) =>
                (await service.CurrentAsync(user.UserId!.Value, PosCookie(http), ct)).ToHttpResult())
            .RequirePermission("pos.view").AllowPosPinSession().WithName("GetPosSession").Produces<PosOperatorSession>();
        session.MapPost("/end", async (HttpContext http, ICurrentUser user, PosPinService service, CancellationToken ct) =>
            {
                if (!http.User.FindAll(AuthMethods.Claim).Any(c => c.Value == AuthMethods.DeviceKey))
                {
                    return Error.Forbidden(Error.Codes.Forbidden, "Só quem entrou por PIN no caixa sai por aqui.").ToProblem();
                }

                await service.EndAsync(user.UserId!.Value, PosCookie(http), ct);
                http.Response.Cookies.Delete(AuthEndpoints.RefreshCookie, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
                return Results.NoContent();
            })
            .RequirePermission("pos.view").AllowPosPinSession().WithName("EndPosSession");

        // "PIN do caixa" da própria pessoa (Meu perfil → Segurança): login completo, pos.create e nenhuma ação reservada.
        var mine = api.MapGroup("/pos/my-pin").WithTags("PDV: sessão do operador").RequireModule("pos");
        mine.MapGet(string.Empty, async (ICurrentUser user, PosPinService service, CancellationToken ct) => Results.Ok(await service.MineAsync(user.UserId!.Value, ct)))
            .RequirePermission("pos.create").WithName("GetMyPosPin").Produces<MyPosPin>();
        mine.MapPut(string.Empty, async (SetPosPinInput input, HttpContext http, ICurrentUser user, PosPinService service, CancellationToken ct) =>
                (await service.SetMineAsync(user.UserId!.Value, ShopfloorDeviceEndpoints.IsFullSession(http.User), input, ct)).ToHttpResult())
            .RequirePermission("pos.create").WithName("SetMyPosPin").Produces<MyPosPin>();
        mine.MapDelete(string.Empty, async (ICurrentUser user, PosPinService service, CancellationToken ct) =>
                (await service.DeleteMineAsync(user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("pos.create").WithName("DeleteMyPosPin");

        // OFF-03 (PP-39): o código de aprovação sem internet do supervisor, separado do PIN; login completo.
        mine.MapPut("/offline-code", async (SetOfflineCodeInput input, HttpContext http, ICurrentUser user, PosOfflineAccessService service, PosPinService pins, CancellationToken ct) =>
            {
                var set = await service.SetMineAsync(user.UserId!.Value, ShopfloorDeviceEndpoints.IsFullSession(http.User), input, ct);
                return set.IsFailure ? set.Error!.ToProblem() : Results.Ok(await pins.MineAsync(user.UserId!.Value, ct));
            })
            .RequirePermission("pos.create").WithName("SetMyPosOfflineCode").Produces<MyPosPin>();
        mine.MapDelete("/offline-code", async (ICurrentUser user, PosOfflineAccessService service, CancellationToken ct) =>
                (await service.DeleteMineAsync(user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("pos.create").WithName("DeleteMyPosOfflineCode");

        // Usuário de operação (ADR 0049): na tela de Usuários, só no Comércio.
        var operation = api.MapGroup("/identity/operation-users").WithTags("Usuários e acessos").RequireModule("identity")
            .RequireProfileFeature("identity", BusinessProfileCatalog.OperationUsersFeature);
        operation.MapGet("/stores", async (OperationUserService service, CancellationToken ct) => (await service.StoresAsync(ct)).ToHttpResult())
            .RequirePermission("identity.create").WithName("ListOperationUserStores").Produces<IReadOnlyList<OperationStoreOption>>();
        operation.MapPost(string.Empty, async (CreateOperationUserRequest request, ICurrentUser user, OperationUserService service, CancellationToken ct) =>
                (await service.CreateAsync(request, user.UserId!.Value, ct)).ToHttpResult(created => Results.Created("/api/v1/identity/users/" + created.Id, created)))
            .RequirePermission("identity.create").WithName("CreateOperationUser").Produces<OperationUserCreated>(StatusCodes.Status201Created);
        operation.MapPut("/{id:guid}", async (Guid id, UpdateOperationUserRequest request, ICurrentUser user, OperationUserService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, request, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("identity.create").WithName("UpdateOperationUser");
        operation.MapPost("/{id:guid}/pin", async (Guid id, ResetOperationPinRequest request, ICurrentUser user, OperationUserService service, CancellationToken ct) =>
                (await service.ResetPinAsync(id, request, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("identity.create").WithName("ResetOperationUserPin");
        return api;
    }

    private static string? PosCookie(HttpContext http) => http.Request.Cookies[PosDeviceCookie.Name];
}
