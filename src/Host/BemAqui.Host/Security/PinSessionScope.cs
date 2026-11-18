using System.Security.Claims;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Web;
using Microsoft.AspNetCore.Authorization;

namespace BemAqui.Host.Security;

/// <summary>
/// Escopo da sessão por PIN (ADR 0026 §1, §3 e §4; PRD-03b CA 10, 15, 16 e 20), logo depois da autenticação. Token com
/// <c>amr = ["pin"]</c> numa rota que exige usuário: primeiro a sessão é conferida (aparelho do cookie válido, sessão por PIN viva,
/// perfil que pode usar PIN) e, se caiu, 401; depois, só passa o endpoint marcado com <c>[PinSessionAllowed]</c>; o resto responde
/// 403 <c>pin_session_scope</c>. Rotas anônimas não usam a identidade e seguem normais.
/// <para>
/// Caixa do PDV (PDV-01 CA 11, ADR 0048): token com <c>amr = ["pin", "hwk"]</c> é a sessão por PIN do aparelho de caixa. Confere o aparelho do cookie
/// <c>__Host-bq_pos</c> e a sessão dele, e só passa o endpoint marcado com <c>[PosPinSessionAllowed]</c>: as duas listas nunca se misturam.
/// </para>
/// </summary>
public sealed class PinSessionScopeMiddleware(RequestDelegate next)
{
    public const string DeviceInvalidCode = "device_not_registered";
    public const string DeviceInvalidMessage = "Este aparelho não está registrado. Entre com e-mail e senha.";
    public const string RegisterDeviceInvalidMessage = "Este aparelho não é mais um caixa registrado. Registre o aparelho de novo.";
    public const string SessionEndedCode = "pin_session_ended";
    public const string SessionEndedMessage = "A entrada por PIN terminou. Entre de novo.";

    public async Task InvokeAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var user = http.User;
        if (user.Identity?.IsAuthenticated != true || !user.FindAll(AuthMethods.Claim).Any(c => c.Value == AuthMethods.Pin))
        {
            await next(http);
            return;
        }

        var endpoint = http.GetEndpoint();
        if (endpoint is null || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            await next(http);
            return;
        }

        var register = user.FindAll(AuthMethods.Claim).Any(c => c.Value == AuthMethods.DeviceKey);
        var validator = http.RequestServices.GetRequiredService<IPinSessionValidator>();
        var check = !Guid.TryParse(user.FindFirstValue("sub"), out var userId) ? PinSessionCheck.SessionEnded
            : register ? await validator.CheckRegisterAsync(userId, http.Request.Cookies[PosDeviceCookie.Name], http.RequestAborted)
            : await validator.CheckAsync(userId, http.Request.Cookies[ShopfloorDeviceCookie.Name], http.RequestAborted);
        if (check != PinSessionCheck.Valid)
        {
            var deviceInvalid = check == PinSessionCheck.DeviceInvalid;
            await Results.Problem(
                    detail: deviceInvalid ? register ? RegisterDeviceInvalidMessage : DeviceInvalidMessage : SessionEndedMessage,
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Sessão encerrada",
                    extensions: new Dictionary<string, object?> { ["code"] = deviceInvalid ? DeviceInvalidCode : SessionEndedCode })
                .ExecuteAsync(http);
            return;
        }

        var allowed = register
            ? endpoint.Metadata.GetMetadata<PosPinSessionAllowedAttribute>() is not null
            : endpoint.Metadata.GetMetadata<PinSessionAllowedAttribute>() is not null;
        if (!allowed)
        {
            await Results.Problem(
                    detail: register ? PinSessionEndpoints.PosScopeMessage : PinSessionEndpoints.ScopeMessage,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Acesso negado",
                    extensions: new Dictionary<string, object?> { ["code"] = PinSessionEndpoints.ScopeCode })
                .ExecuteAsync(http);
            return;
        }

        await next(http);
    }
}
