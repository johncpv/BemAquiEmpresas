using System.Security.Claims;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application.Shopfloor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

/// <summary>
/// Aparelho da fábrica e PIN (PRD-03b, ADR 0026), no módulo <c>shopfloor</c>: o <c>/fab</c> vê o aparelho e troca o tema com
/// <c>shopfloor.view</c>; registrar exige <c>production.edit</c> e <c>shopfloor.create</c> e login completo; criar PIN exige
/// <c>shopfloor.create</c> e login completo. Configurações → Segurança lista com <c>settings.view</c> e revoga com <c>settings.edit</c> ou
/// <c>production.approve</c>. Meu perfil → Segurança lista e apaga os meus PINs.
/// </summary>
internal static class ShopfloorDeviceEndpoints
{
    public static IEndpointRouteBuilder MapShopfloorDeviceEndpoints(this IEndpointRouteBuilder api)
    {
        var device = api.MapGroup("/shopfloor/device").WithTags("App do operador").RequireModule("shopfloor");
        device.MapGet(string.Empty, async (HttpContext http, ICurrentUser user, ShopfloorDeviceService service, CancellationToken ct) =>
                Results.Ok(await service.ContextAsync(DeviceSecret(http), user.UserId!.Value, IsFullSession(http.User), ct)))
            .RequirePermission("shopfloor.view").AllowPinSession().WithName("GetShopfloorDevice").Produces<ShopfloorDeviceContext>();
        // CA 6: o login completo usado para registrar não deixa sessão persistente no tablet; o refresh que houver é revogado aqui.
        device.MapPost(string.Empty, async (RegisterDeviceInput input, HttpContext http, ICurrentUser user, ShopfloorDeviceService service, BemAqui.Identity.Application.AuthService auth, CancellationToken ct) =>
            {
                var result = await service.RegisterAsync(input, DeviceSecret(http), user.UserId!.Value, IsFullSession(http.User), ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                await auth.LogoutAsync(http.Request.Cookies[AuthEndpoints.RefreshCookie], AuthEndpoints.Ip(http), ct);
                http.Response.Cookies.Delete(AuthEndpoints.RefreshCookie, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
                AuthEndpoints.SetDeviceCookie(http, result.Value.Secret, result.Value.ExpiresAt);
                return Results.Created("/api/v1/shopfloor/device", result.Value.Device);
            })
            .RequirePermission("production.edit").RequirePermission("shopfloor.create")
            .WithName("RegisterShopfloorDevice").Produces<ShopfloorDeviceContext>(StatusCodes.Status201Created);
        device.MapPut("/theme", async (DeviceThemeInput input, HttpContext http, ICurrentUser user, ShopfloorDeviceService service, CancellationToken ct) =>
                (await service.SetThemeAsync(DeviceSecret(http), user.UserId!.Value, IsFullSession(http.User), input.Theme, ct)).ToHttpResult())
            .RequirePermission("shopfloor.view").AllowPinSession().WithName("SetShopfloorDeviceTheme").Produces<ShopfloorDeviceContext>();
        device.MapPost("/pin", async (CreatePinInput input, HttpContext http, ICurrentUser user, PinService pins, CancellationToken ct) =>
                (await pins.CreateAsync(DeviceSecret(http), user.UserId!.Value, IsFullSession(http.User), input, ct)).ToHttpResult())
            .RequirePermission("shopfloor.create").WithName("CreateShopfloorPin").Produces<ShopfloorDeviceContext>();
        device.MapDelete("/pin", async (HttpContext http, ICurrentUser user, PinService pins, CancellationToken ct) =>
                (await pins.DeleteOnDeviceAsync(DeviceSecret(http), user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("shopfloor.view").AllowPinSession().WithName("DeleteMyShopfloorPin");

        var devices = api.MapGroup("/settings/security/shopfloor-devices").WithTags("Configurações").RequireModule("settings");
        devices.MapGet(string.Empty, async (ShopfloorDeviceService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .RequirePermission("settings.view").WithName("ListShopfloorDevices").Produces<ShopfloorDeviceList>();
        devices.MapPost("/{id:guid}/revoke", async (Guid id, ICurrentUser user, ShopfloorDeviceService service, CancellationToken ct) =>
                (await service.RevokeAsync(id, user.UserId!.Value, ct)).ToHttpResult())
            .RequireAnyPermissionOf("settings.edit", "production.approve").WithName("RevokeShopfloorDevice").Produces<ShopfloorDeviceList>();

        // Meus PINs (Meu perfil → Segurança): dado da própria pessoa, como /me/2fa.
        var mine = api.MapGroup("/me/pins").WithTags("Identidade");
        mine.MapGet(string.Empty, async (ICurrentUser user, PinService pins, CancellationToken ct) => Results.Ok(await pins.MineAsync(user.UserId!.Value, ct)))
            .WithName("ListMyPins").Produces<IReadOnlyList<MyPin>>();
        mine.MapDelete("/{deviceId:guid}", async (Guid deviceId, ICurrentUser user, PinService pins, CancellationToken ct) =>
                (await pins.DeleteAsync(user.UserId!.Value, deviceId, ct)).ToHttpResult())
            .WithName("DeleteMyPin");
        return api;
    }

    private static string? DeviceSecret(HttpContext http) => http.Request.Cookies[ShopfloorDeviceCookie.Name];

    /// <summary>Sessão completa (e-mail e senha): o <c>amr</c> não tem <c>pin</c>.</summary>
    internal static bool IsFullSession(ClaimsPrincipal user) => !user.FindAll(AuthMethods.Claim).Any(c => c.Value == AuthMethods.Pin);
}