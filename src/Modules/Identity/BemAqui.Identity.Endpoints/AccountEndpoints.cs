using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

public sealed record ForgotPasswordRequest(string Email);

public sealed record MessageResponse(string Message);

public sealed record ResetPasswordRequest(string Token, string Password);

public sealed record InviteTokenRequest(string Token);

public sealed record AcceptInviteRequest(string Token, string? Name, string Password);

public sealed record InviteAcceptedResponse(string Email, bool OfferTwoFactor, string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Esqueci minha senha, redefinição e convite (FND-04b). O token vai sempre no corpo, nunca na URL da API, para
/// não aparecer em log de acesso.
/// </summary>
internal static class AccountEndpoints
{
    public static RouteGroupBuilder MapAccountEndpoints(this RouteGroupBuilder auth)
    {
        auth.MapPost("/forgot-password", async (ForgotPasswordRequest request, AccountRecovery recovery, CancellationToken ct) =>
                Results.Accepted(value: new MessageResponse(await recovery.ForgotAsync(request.Email, ct))))
            .WithName("ForgotPassword");

        auth.MapPost("/reset-password", async (ResetPasswordRequest request, HttpContext http, AccountRecovery recovery, CancellationToken ct) =>
                (await recovery.ResetAsync(request.Token, request.Password, AuthEndpoints.Ip(http), ct)).ToHttpResult())
            .WithName("ResetPassword");

        auth.MapPost("/invites/inspect", async (InviteTokenRequest request, Invites invites, CancellationToken ct) =>
                Results.Ok(await invites.GetAsync(request.Token, ct)))
            .WithName("InspectInvite");

        // Aceitou: já entra (cookie de refresh + access), e a tela oferece o 2FA como próximo passo (CA 17).
        auth.MapPost("/invites/accept", async (AcceptInviteRequest request, HttpContext http, Invites invites, AuthService service, CancellationToken ct) =>
            {
                var ip = AuthEndpoints.Ip(http);
                var accepted = await invites.AcceptAsync(request.Token, request.Name, request.Password, ip, ct);
                if (accepted.IsFailure)
                {
                    return accepted.ToHttpResult();
                }

                var login = await service.LoginAsync(accepted.Value.Email, request.Password, http.Request.Host.Host, ip, ct);
                return login.ToHttpResult(session =>
                {
                    AuthEndpoints.Issue(http, session);
                    return Results.Ok(new InviteAcceptedResponse(accepted.Value.Email, accepted.Value.OfferTwoFactor, session.AccessToken, session.AccessTokenExpiresAt));
                });
            })
            .WithName("AcceptInvite");

        return auth;
    }
}
