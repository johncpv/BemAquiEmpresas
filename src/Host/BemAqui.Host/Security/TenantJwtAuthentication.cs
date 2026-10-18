using System.Security.Claims;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace BemAqui.Host.Security;

public static class TenantJwtAuthentication
{
    public const string TenantClaim = "tid";
    public const string StampClaim = "sstamp";

    /// <summary>
    /// JWT do tenant: além de assinatura, emissor e validade, exige <c>tid</c> = tenant do host, <c>aud</c> = host
    /// do request e <c>sstamp</c> = security stamp atual do usuário (ADR 0008; FND-02 CA 6, FND-04 CA 16). Toda
    /// rota exige usuário autenticado, salvo as anônimas.
    /// </summary>
    public static IServiceCollection AddTenantJwtAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var signing = new TokenSigning(configuration, environment);
        services.AddSingleton(signing);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = signing.Options.Issuer,
                    ValidateAudience = false, // conferido contra o host do request em OnTokenValidated
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signing.Key,
                    NameClaimType = "sub",
                };
                jwt.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // WebSocket do SignalR não envia Authorization: o token vem na query, só para /hubs.
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var tenant = context.HttpContext.RequestServices.GetRequiredService<TenantContextHolder>().Tenant;
                        var principal = context.Principal;
                        var host = TenantDomain.Normalize(context.HttpContext.Request.Host.Host);
                        var audiences = principal?.FindAll("aud").Select(c => c.Value).ToList() ?? [];
                        if (tenant is null
                            || !Guid.TryParse(principal?.FindFirstValue(TenantClaim), out var tokenTenant) || tokenTenant != tenant.Id
                            || !audiences.Contains(host, StringComparer.Ordinal))
                        {
                            context.Fail("Token de outro tenant ou de outro host.");
                            return;
                        }

                        var stamps = context.HttpContext.RequestServices.GetService<ISecurityStampValidator>();
                        if (stamps is not null
                            && (!Guid.TryParse(principal?.FindFirstValue("sub"), out var userId)
                                || principal?.FindFirstValue(StampClaim) is not { } stamp
                                || !await stamps.IsCurrentAsync(userId, stamp, context.HttpContext.RequestAborted)))
                        {
                            context.Fail("Sessão encerrada: o usuário foi desativado ou mudou a senha.");
                        }
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }
}
