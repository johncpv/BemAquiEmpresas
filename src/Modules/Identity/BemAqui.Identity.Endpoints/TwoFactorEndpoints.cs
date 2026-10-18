using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BemAqui.Identity.Endpoints;

public sealed record TwoFactorStep(string Challenge, string Purpose, DateTimeOffset ExpiresAt)
{
    public static TwoFactorStep? From(LoginOutcome outcome) =>
        outcome.TwoFactor is { } p ? new TwoFactorStep(p.Challenge, p.Purpose.ToString().ToLowerInvariant(), p.ExpiresAt) : null;
}

/// <summary>Login: <c>accessToken</c> quando entrou, ou <c>twoFactor</c> quando falta o código ou a configuração.</summary>
public sealed record LoginResponse(string? AccessToken, DateTimeOffset? ExpiresAt, TwoFactorStep? TwoFactor);

public sealed record ChallengeRequest(string Challenge);

public sealed record VerifyTwoFactorRequest(string Challenge, string? Code, string? RecoveryCode);

public sealed record ConfirmWithChallengeRequest(string Challenge, string Code);

public sealed record ConfirmTwoFactorRequest(string Code);

public sealed record DisableTwoFactorRequest(string Password, string Code);

public sealed record TwoFactorActivatedResponse(IReadOnlyList<string> RecoveryCodes, string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// 2FA (FND-05). Desafio do login e configuração obrigatória: anônimas, pelo token do desafio (5 min, só serve
/// aqui). Perfil: autenticadas, em <c>/me/2fa</c>.
/// </summary>
internal static class TwoFactorEndpoints
{
    public static RouteGroupBuilder MapTwoFactorChallengeEndpoints(this RouteGroupBuilder auth)
    {
        var twoFactor = auth.MapGroup("/2fa");

        twoFactor.MapPost("/verify", async (VerifyTwoFactorRequest request, HttpContext http, TwoFactorService service, CancellationToken ct) =>
                (await service.VerifyAsync(request.Challenge, request.Code, request.RecoveryCode, http.Request.Host.Host, AuthEndpoints.Ip(http), ct))
                    .ToHttpResult(session => AuthEndpoints.Issue(http, session)))
            .WithName("VerifyTwoFactor").Produces<AccessTokenResponse>();

        twoFactor.MapPost("/setup", async (ChallengeRequest request, TwoFactorService service, CancellationToken ct) =>
                (await service.BeginSetupWithChallengeAsync(request.Challenge, ct)).ToHttpResult())
            .WithName("BeginRequiredTwoFactorSetup").Produces<TotpSetup>();

        twoFactor.MapPost("/confirm", async (ConfirmWithChallengeRequest request, HttpContext http, TwoFactorService service, CancellationToken ct) =>
                (await service.ConfirmSetupWithChallengeAsync(request.Challenge, request.Code, http.Request.Host.Host, AuthEndpoints.Ip(http), ct))
                    .ToHttpResult(activated => Activated(http, activated)))
            .WithName("ConfirmRequiredTwoFactorSetup").Produces<TwoFactorActivatedResponse>();

        return auth;
    }

    public static IEndpointRouteBuilder MapProfileTwoFactorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/me/2fa").WithTags("Identidade").RequireAuthorization();

        me.MapGet(string.Empty, async (ICurrentUser user, TwoFactorService service, CancellationToken ct) =>
                await service.StatusAsync(user.UserId!.Value, ct) is { } status ? Results.Ok(status) : Results.NotFound())
            .WithName("GetTwoFactorStatus").Produces<TwoFactorStatus>();

        me.MapPost("/setup", async (ICurrentUser user, TwoFactorService service, CancellationToken ct) =>
                (await service.BeginSetupAsync(user.UserId!.Value, ct)).ToHttpResult())
            .WithName("BeginTwoFactorSetup").Produces<TotpSetup>();

        me.MapPost("/confirm", async (ConfirmTwoFactorRequest request, ICurrentUser user, HttpContext http, TwoFactorService service, CancellationToken ct) =>
                (await service.ConfirmSetupAsync(user.UserId!.Value, request.Code, http.Request.Host.Host, AuthEndpoints.Ip(http), ct))
                    .ToHttpResult(activated => Activated(http, activated)))
            .WithName("ConfirmTwoFactorSetup").Produces<TwoFactorActivatedResponse>();

        me.MapPost("/disable", async (DisableTwoFactorRequest request, ICurrentUser user, HttpContext http, TwoFactorService service, CancellationToken ct) =>
                (await service.DisableAsync(user.UserId!.Value, request.Password, request.Code, http.Request.Host.Host, AuthEndpoints.Ip(http), ct))
                    .ToHttpResult(session => AuthEndpoints.Issue(http, session)))
            .WithName("DisableTwoFactor").Produces<AccessTokenResponse>();

        return endpoints;
    }

    /// <summary>Resposta do login: entra (cookie + access) ou devolve o passo de 2FA, sem cookie.</summary>
    public static LoginResponse Login(HttpContext http, LoginOutcome outcome)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (outcome.Session is { } session)
        {
            AuthEndpoints.SetSession(http, session);
        }

        return new LoginResponse(outcome.Session?.AccessToken, outcome.Session?.AccessTokenExpiresAt, TwoFactorStep.From(outcome));
    }

    private static IResult Activated(HttpContext http, TwoFactorActivated activated)
    {
        AuthEndpoints.SetSession(http, activated.Session);
        return Results.Ok(new TwoFactorActivatedResponse(activated.RecoveryCodes, activated.Session.AccessToken, activated.Session.AccessTokenExpiresAt));
    }
}
