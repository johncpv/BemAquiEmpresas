using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application.Shopfloor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

/// <summary>
/// Aparelhos de caixa do PDV (PDV-01 CA 4 a 8, ADR 0048), no módulo <c>pos</c> (oculto para a fábrica): listar, registrar e bloquear com
/// <c>pos.manage_devices</c>. Registrar exige login completo e TOTP recente (403 <c>two_factor_required</c> abre o "Confirme o código" e repete);
/// o segredo vai só para o cookie <c>__Host-bq_pos</c>. A lista geral de aparelhos (com a coluna Tipo) continua em Configurações → Segurança.
/// </summary>
internal static class RegisterDeviceEndpoints
{
    public static IEndpointRouteBuilder MapRegisterDeviceEndpoints(this IEndpointRouteBuilder api)
    {
        var devices = api.MapGroup("/identity/devices").WithTags("Aparelhos do PDV").RequireModule("pos");
        devices.MapGet(string.Empty, async (Guid? storeId, RegisterDeviceService service, CancellationToken ct) => Results.Ok(await service.ListAsync(storeId, ct)))
            .RequirePermission("pos.manage_devices").WithName("ListPosDevices").Produces<IReadOnlyList<PosDeviceView>>();
        // O login completo usado para registrar no aparelho da loja não deixa sessão persistente: o refresh que houver é revogado (como na fábrica).
        devices.MapPost(string.Empty, async (RegisterPosDeviceInput input, HttpContext http, ICurrentUser user, RegisterDeviceService service, BemAqui.Identity.Application.AuthService auth, CancellationToken ct) =>
            {
                var result = await service.RegisterAsync(input, http.Request.Cookies[PosDeviceCookie.Name], user.UserId!.Value, ShopfloorDeviceEndpoints.IsFullSession(http.User), ct);
                if (result.IsFailure)
                {
                    return result.Error!.ToProblem();
                }

                await auth.LogoutAsync(http.Request.Cookies[AuthEndpoints.RefreshCookie], AuthEndpoints.Ip(http), ct);
                http.Response.Cookies.Delete(AuthEndpoints.RefreshCookie, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
                DeviceEndpoints.SetCookie(http, result.Value.Secret, result.Value.ExpiresAt);
                return Results.Created("/api/v1/identity/devices/" + result.Value.Device.Id, result.Value.Device);
            })
            .RequirePermission("pos.manage_devices").RequireRecentTwoFactor().WithName("RegisterPosDevice").Produces<PosDeviceView>(StatusCodes.Status201Created);
        devices.MapDelete("/{id:guid}", async (Guid id, ICurrentUser user, RegisterDeviceService service, CancellationToken ct) =>
                (await service.RevokeAsync(id, user.UserId!.Value, ct)).ToHttpResult())
            .RequirePermission("pos.manage_devices").WithName("RevokePosDevice");
        return api;
    }
}
