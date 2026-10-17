using System.Security.Claims;
using System.Security.Cryptography;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace BemAqui.Host.Security;

/// <summary>Seção <c>Auth</c>. A emissão de tokens e a gestão da chave chegam na FND-04.</summary>
public sealed class AuthOptions
{
    public const string Section = "Auth";

    public string Issuer { get; set; } = "bemaqui-empresas";

    /// <summary>Chave HMAC em base64 (no mínimo 32 bytes).</summary>
    public string? SigningKey { get; set; }
}

public static class TenantJwtAuthentication
{
    public const string TenantClaim = "tid";

    /// <summary>
    /// JWT do tenant: além de assinatura, emissor e validade, o <c>tid</c> tem de ser o tenant resolvido pelo
    /// host e o <c>aud</c> o próprio host, no REST e no hub SignalR (ADR 0008, FND-02 CA 6). Toda rota exige
    /// usuário autenticado, salvo as marcadas como anônimas.
    /// </summary>
    public static IServiceCollection AddTenantJwtAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var options = configuration.GetSection(AuthOptions.Section).Get<AuthOptions>() ?? new AuthOptions();
        var key = SigningKey(options, environment);

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = false, // conferido contra o host do request em OnTokenValidated
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = key,
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
                    OnTokenValidated = context =>
                    {
                        var tenant = context.HttpContext.RequestServices.GetRequiredService<TenantContextHolder>().Tenant;
                        var tid = context.Principal?.FindFirstValue(TenantClaim);
                        var audiences = context.Principal?.FindAll("aud").Select(c => c.Value).ToList() ?? [];
                        var host = TenantDomain.Normalize(context.HttpContext.Request.Host.Host);
                        if (tenant is null || !Guid.TryParse(tid, out var tokenTenant) || tokenTenant != tenant.Id || !audiences.Contains(host, StringComparer.Ordinal))
                        {
                            context.Fail("Token de outro tenant ou de outro host.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    private static SymmetricSecurityKey SigningKey(AuthOptions options, IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(options.SigningKey))
        {
            var bytes = Convert.FromBase64String(options.SigningKey);
            return bytes.Length >= 32
                ? new SymmetricSecurityKey(bytes)
                : throw new InvalidOperationException("Auth:SigningKey precisa ter pelo menos 32 bytes (base64).");
        }

        if (environment.IsDevelopment())
        {
            return new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)); // só desenvolvimento: muda a cada subida
        }

        throw new InvalidOperationException("Auth:SigningKey não configurada (secret). A gestão de chaves chega na FND-04.");
    }
}
