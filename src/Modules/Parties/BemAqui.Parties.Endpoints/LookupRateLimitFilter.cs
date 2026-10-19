using System.Threading.RateLimiting;
using BemAqui.BuildingBlocks.Lookups;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BemAqui.Parties.Endpoints;

/// <summary>
/// 30 consultas de CNPJ ou CEP por minuto por usuário + tenant (CAD-01b CA 12). Fica no endpoint, depois da autenticação,
/// porque o rate limiter global roda antes e não conhece o usuário.
/// </summary>
internal sealed class LookupRateLimitFilter : IEndpointFilter
{
    public const string Message = "Muitas consultas seguidas. Aguarde um minuto.";

    private static PartitionedRateLimiter<string>? _limiter;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var permits = http.RequestServices.GetRequiredService<IOptions<LookupOptions>>().Value.PerUserPerMinute;
        _limiter ??= PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key, _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, permits), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        var key = (http.RequestServices.GetService<ITenantContext>()?.Tenant?.Id.ToString() ?? "-") + "|" + (http.User.FindFirst("sub")?.Value ?? "-");
        using var lease = _limiter.AttemptAcquire(key);
        if (!lease.IsAcquired)
        {
            http.Response.Headers.RetryAfter = "60";
            return Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: Message, detail: Message, extensions: new Dictionary<string, object?> { ["code"] = "rate_limited" });
        }

        return await next(context);
    }
}
