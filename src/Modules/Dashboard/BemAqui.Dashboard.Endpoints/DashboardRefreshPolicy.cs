using System.Globalization;
using System.Threading.RateLimiting;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Dashboard.Endpoints;

/// <summary>
/// Política nomeada <c>dashboard-refresh</c> (DSH-01a CA 13, ADR 0028 §3): "Atualizar" uma vez a cada 10 s por usuário do
/// tenant; acima, 429 com "Aguarde alguns segundos para atualizar de novo." e <c>Retry-After</c>.
/// </summary>
public sealed class DashboardRefreshPolicy : IRateLimiterPolicy<string>
{
    public const string Name = "dashboard-refresh";
    public const string Message = "Aguarde alguns segundos para atualizar de novo.";
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } = async (context, _) =>
    {
        var wait = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry) ? retry : Window;
        context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        await Results.Problem(statusCode: StatusCodes.Status429TooManyRequests, title: Message, detail: Message,
            extensions: new Dictionary<string, object?> { ["code"] = "dashboard_refresh_rate_limited" }).ExecuteAsync(context.HttpContext);
    };

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var tenant = httpContext.RequestServices.GetService<ITenantContext>()?.Tenant?.Id.ToString() ?? "-";
        var user = httpContext.User.FindFirst("sub")?.Value ?? "-";
        // Uma janela por rota: o "Atualizar" do painel do dono não gasta a vez do painel do supervisor.
        var route = httpContext.Request.Path.Value ?? "-";
        return RateLimitPartition.GetFixedWindowLimiter("dashboard-refresh|" + tenant + "|" + user + "|" + route, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = Window,
            QueueLimit = 0,
        });
    }
}
