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
/// </summary>
public sealed class PinSessionScopeMiddleware(RequestDelegate next)
{
    public const string DeviceInvalidCode = "device_not_registered";
    public const string DeviceInvalidMessage = "Este aparelho não está registrado. Entre com e-mail e senha.";
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

        var check = Guid.TryParse(user.FindFirstValue("sub"), out var userId)
            ? await http.RequestServices.GetRequiredService<IPinSessionValidator>().CheckAsync(userId, http.Request.Cookies[ShopfloorDeviceCookie.Name], http.RequestAborted)
            : PinSessionCheck.SessionEnded;
        if (check != PinSessionCheck.Valid)
        {
            var deviceInvalid = check == PinSessionCheck.DeviceInvalid;
            await Results.Problem(
                    detail: deviceInvalid ? DeviceInvalidMessage : SessionEndedMessage,
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Sessão encerrada",
                    extensions: new Dictionary<string, object?> { ["code"] = deviceInvalid ? DeviceInvalidCode : SessionEndedCode })
                .ExecuteAsync(http);
            return;
        }

        if (endpoint.Metadata.GetMetadata<PinSessionAllowedAttribute>() is null)
        {
            await Results.Problem(
                    detail: PinSessionEndpoints.ScopeMessage,
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Acesso negado",
                    extensions: new Dictionary<string, object?> { ["code"] = PinSessionEndpoints.ScopeCode })
                .ExecuteAsync(http);
            return;
        }

        await next(http);
    }
}