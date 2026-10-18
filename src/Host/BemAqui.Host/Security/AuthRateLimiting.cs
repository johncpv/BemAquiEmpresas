using System.Globalization;
using System.Threading.RateLimiting;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.RateLimiting;

namespace BemAqui.Host.Security;

/// <summary>Limites das rotas anônimas de autenticação, por minuto (FND-04 CA 11).</summary>
public sealed class AuthRateLimitOptions
{
    public const string Section = "RateLimit:Auth";

    /// <summary>Por IP real (do forwarded header confiável) dentro do tenant.</summary>
    public int PerIpPerMinute { get; set; } = 20;

    /// <summary>Teto do tenant inteiro, contra ataque distribuído em muitos IPs.</summary>
    public int PerTenantPerMinute { get; set; } = 300;
}

/// <summary>
/// Rate limit de login, refresh, logout, recuperação de senha e convite (<c>/api/v1/auth/*</c>): duas janelas fixas
/// encadeadas, IP + tenant e tenant. Excedeu: 429 com <c>Retry-After</c>. O limite por usuário é o bloqueio da conta.
/// </summary>
public static class AuthRateLimiting
{
    public const string PathPrefix = "/api/v1/auth";
    public const string Message = "Muitas tentativas em pouco tempo. Aguarde um pouco e tente de novo.";

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthRateLimitOptions.Section).Get<AuthRateLimitOptions>() ?? new AuthRateLimitOptions();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                Partition(options.PerIpPerMinute, http => TenantKey(http) + "|" + (http.Connection.RemoteIpAddress?.ToString() ?? "-")),
                Partition(options.PerTenantPerMinute, TenantKey));
            o.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
                context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                await Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: Message,
                    extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" })
                    .ExecuteAsync(context.HttpContext);
            };
        });
        return services;
    }

    private static PartitionedRateLimiter<HttpContext> Partition(int permits, Func<HttpContext, string> key) =>
        PartitionedRateLimiter.Create<HttpContext, string>(http =>
            IsAuthRoute(http)
                ? RateLimitPartition.GetFixedWindowLimiter(key(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permits,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                })
                : RateLimitPartition.GetNoLimiter(string.Empty));

    /// <summary>Login da plataforma tem o mesmo limite (FND-13 CA 7).</summary>
    public const string PlatformPathPrefix = "/api/v1/platform/auth";

    private static bool IsAuthRoute(HttpContext http) =>
        http.Request.Path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase)
        || http.Request.Path.StartsWithSegments(PlatformPathPrefix, StringComparison.OrdinalIgnoreCase);

    private static string TenantKey(HttpContext http) =>
        http.RequestServices.GetService<ITenantContext>()?.Tenant?.Id.ToString() ?? "-";
}
