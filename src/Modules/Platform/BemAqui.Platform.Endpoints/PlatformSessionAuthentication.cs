using System.Security.Claims;
using System.Text.Encodings.Web;
using BemAqui.Platform.Application.Auth;
using BemAqui.Platform.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.Platform.Endpoints;

/// <summary>
/// Esquema <c>Platform</c> (FND-13): só o cookie opaco da sessão do super admin, conferido no catálogo. Ignora o
/// cabeçalho Authorization, então um token de tenant nunca vale aqui, e o cookie <c>__Host-</c> nunca sai do host
/// da plataforma (CA 4).
/// </summary>
public sealed class PlatformSessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Platform";
    public const string Policy = "platform";
    public const string Cookie = "__Host-bq_pa";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = Request.Cookies[Cookie];
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var service = Context.RequestServices.GetRequiredService<PlatformAuthService>();
        var admin = await service.AuthenticateAsync(token, TenantDomain.Normalize(Request.Host.Host), Context.RequestAborted);
        if (admin is null)
        {
            return AuthenticateResult.Fail("Sessão da plataforma expirada ou inválida.");
        }

        var identity = new ClaimsIdentity([new Claim("sub", admin.Id.ToString()), new Claim("typ", "platform")], SchemeName, "sub", null);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    public static void Register(IServiceCollection services)
    {
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, PlatformSessionAuthenticationHandler>(SchemeName, _ => { });
        services.AddAuthorizationBuilder().AddPolicy(Policy, p => p.AddAuthenticationSchemes(SchemeName).RequireAuthenticatedUser());
    }
}
