using System.Diagnostics;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application.Kiosk;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

/// <summary>Troca do token da TV: o token vem do fragmento <c>/tv#{token}</c>, no corpo, nunca no caminho nem na query.</summary>
public sealed record KioskSessionRequest(string Token);

/// <summary>Versão vista pela pessoa (xmin) para gerar novo link ou revogar.</summary>
public sealed record KioskVersionInput(uint Version);

/// <summary>
/// Painel da TV (PNL-01a, ADR 0030), no módulo <c>kiosk</c>. Gestão (<c>/api/v1/kiosk-screens</c>): listar com <c>kiosk.view</c>; criar,
/// editar e gerar novo link com <c>kiosk.create</c> e <c>production.view</c>; revogar com <c>kiosk.delete</c>. Quiosque
/// (<c>/api/v1/kiosk</c>): <c>POST /session</c> anônima (troca o token pelo cookie, rate limit <c>kiosk-session</c>, tempo e mensagem
/// uniformes) e <c>GET /screen</c> só com o principal de quiosque.
/// </summary>
internal static class KioskScreenEndpoints
{
    public static IEndpointRouteBuilder MapKioskEndpoints(this IEndpointRouteBuilder api)
    {
        var screens = api.MapGroup("/kiosk-screens").WithTags("Painel da TV").RequireModule("kiosk");
        screens.MapGet(string.Empty, async (KioskScreenService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("kiosk.view").WithName("ListKioskScreens").Produces<KioskScreenList>();
        screens.MapPost(string.Empty, async (KioskScreenInput input, HttpContext http, ICurrentUser user, KioskScreenService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(input, http.Request.Host.Host, user.UserId!.Value, ct);
                http.Response.Headers.CacheControl = "no-store"; // o link aparece uma única vez
                return result.ToHttpResult(link => Results.Created($"/api/v1/kiosk-screens/{link.Screen.Id}", link));
            })
            .RequirePermission("kiosk.create").RequirePermission("production.view").WithName("CreateKioskScreen").Produces<KioskScreenLink>(StatusCodes.Status201Created);
        screens.MapPut("/{id:guid}", async (Guid id, KioskScreenInput input, ICurrentUser user, KioskScreenService service, CancellationToken ct) =>
                (await service.UpdateAsync(id, input, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("kiosk.create").RequirePermission("production.view").WithName("UpdateKioskScreen").Produces<KioskScreenRow>();
        screens.MapPost("/{id:guid}/new-link", async (Guid id, KioskVersionInput input, HttpContext http, ICurrentUser user, KioskScreenService service, CancellationToken ct) =>
            {
                var result = await service.NewLinkAsync(id, input.Version, http.Request.Host.Host, user.UserId!.Value, ct);
                http.Response.Headers.CacheControl = "no-store";
                return result.ToHttpResult();
            })
            .RequirePermission("kiosk.create").RequirePermission("production.view").WithName("RegenerateKioskScreenLink").Produces<KioskScreenLink>();
        screens.MapPost("/{id:guid}/revoke", async (Guid id, KioskVersionInput input, ICurrentUser user, KioskScreenService service, CancellationToken ct) =>
                (await service.RevokeAsync(id, input.Version, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("kiosk.delete").WithName("RevokeKioskScreen").Produces<KioskScreenRow>();

        var kiosk = api.MapGroup("/kiosk").WithTags("Painel da TV").RequireModule("kiosk");
        // Rota anônima do quiosque (ADR 0009 adendo de 21/10/2026): Origin do host e X-CSRF, 10 tentativas por minuto por IP e tenant,
        // 401 com tempo e mensagem únicos. O cookie sai HttpOnly, Secure, SameSite=Strict e Path=/.
        kiosk.MapPost("/session", async (KioskSessionRequest? request, HttpContext http, KioskSessionService sessions, TimeProvider clock, CancellationToken ct) =>
            {
                http.Response.Headers.CacheControl = "no-store";
                var watch = Stopwatch.StartNew();
                if (!AuthEndpoints.SameOriginWithCsrfHeader(http))
                {
                    await UniformAsync(watch, clock, ct);
                    return Invalid();
                }

                var result = await sessions.OpenAsync(request?.Token, http.Connection.RemoteIpAddress, ct);
                switch (result.Status)
                {
                    case KioskOpenStatus.Opened:
                        http.Response.Cookies.Append(KioskCookie.Name, result.Secret!, new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = true,
                            SameSite = SameSiteMode.Strict,
                            Path = "/",
                            Expires = result.ExpiresAt,
                            IsEssential = true,
                        });
                        return Results.Ok(new KioskSessionOpened(result.ScreenName!));
                    case KioskOpenStatus.NetworkDenied:
                        await UniformAsync(watch, clock, ct);
                        return Results.Problem(detail: KioskRules.NetworkMessage, statusCode: StatusCodes.Status403Forbidden, title: "Acesso negado",
                            extensions: new Dictionary<string, object?> { ["code"] = "kiosk_network" });
                    default:
                        await UniformAsync(watch, clock, ct);
                        return Invalid();
                }
            })
            .AllowAnonymous().RequireRateLimiting(KioskEndpoints.SessionRateLimitPolicy).WithName("OpenKioskSession").Produces<KioskSessionOpened>();
        kiosk.MapGet("/screen", (HttpContext http, KioskSessionService sessions) => Results.Ok(sessions.Context(KioskPrincipal.From(http.User))))
            .RequireKiosk().WithName("GetKioskScreen").Produces<KioskScreenContext>();
        return api;
    }

    private static IResult Invalid() =>
        Results.Problem(detail: KioskRules.InvalidLinkMessage, statusCode: StatusCodes.Status401Unauthorized, title: "Link inválido",
            extensions: new Dictionary<string, object?> { ["code"] = "kiosk_link_invalid" });

    /// <summary>Falha sempre com o mesmo tempo mínimo: token desconhecido, usado, revogado ou de outro tenant não se distinguem.</summary>
    private static async Task UniformAsync(Stopwatch watch, TimeProvider clock, CancellationToken ct)
    {
        var remaining = KioskRules.UniformFailure - watch.Elapsed;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, clock, ct);
        }
    }
}
