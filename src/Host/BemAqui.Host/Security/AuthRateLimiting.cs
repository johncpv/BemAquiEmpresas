using System.Globalization;
using System.Threading.RateLimiting;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Production.Application;
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

    /// <summary>Pergunte ao BemAqui (FND-15), por IP + tenant: cada pergunta pode virar uma chamada paga ao provedor de IA.</summary>
    public int AskPerMinute { get; set; } = 10;

    /// <summary>Logo público do tenant (CAD-06 CA 7), por IP + tenant.</summary>
    public int LogoPerMinute { get; set; } = 60;

    /// <summary>Troca do token da TV pelo cookie (PNL-01a CA 8, política nomeada <c>kiosk-session</c>), por IP + tenant.</summary>
    public int KioskSessionPerMinute { get; set; } = 10;
}

/// <summary>
/// Rate limit de login, refresh, logout, recuperação de senha e convite (<c>/api/v1/auth/*</c>): duas janelas fixas
/// encadeadas, IP + tenant e tenant. Excedeu: 429 com <c>Retry-After</c>. O limite por usuário é o bloqueio da conta.
/// </summary>
public static class AuthRateLimiting
{
    public const string PathPrefix = "/api/v1/auth";
    public const string AskPath = "/api/v1/ajuda/perguntar";
    public const string LogoPath = "/api/v1/public/tenant-logo";
    public const string Message = "Muitas tentativas em pouco tempo. Aguarde um pouco e tente de novo.";

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthRateLimitOptions.Section).Get<AuthRateLimitOptions>() ?? new AuthRateLimitOptions();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                Partition(options.PerIpPerMinute, http => TenantKey(http) + "|" + (http.Connection.RemoteIpAddress?.ToString() ?? "-")),
                Partition(options.PerTenantPerMinute, TenantKey),
                PartitionedRateLimiter.Create<HttpContext, string>(http =>
                    http.Request.Path.StartsWithSegments(AskPath, StringComparison.OrdinalIgnoreCase)
                        ? RateLimitPartition.GetFixedWindowLimiter("ask|" + TenantKey(http) + "|" + (http.Connection.RemoteIpAddress?.ToString() ?? "-"), _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = options.AskPerMinute,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        })
                        : RateLimitPartition.GetNoLimiter(string.Empty)),
                PartitionedRateLimiter.Create<HttpContext, string>(http =>
                    http.Request.Path.StartsWithSegments(LogoPath, StringComparison.OrdinalIgnoreCase)
                        ? RateLimitPartition.GetFixedWindowLimiter("logo|" + TenantKey(http) + "|" + (http.Connection.RemoteIpAddress?.ToString() ?? "-"), _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = options.LogoPerMinute,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        })
                        : RateLimitPartition.GetNoLimiter(string.Empty)));
            // Simulações do motor (PLN-02 CA 10, PLN-05 CA 10): política nomeada, por usuário dentro do tenant e por rota (a prévia da fila e a
            // simulação do orçamento contam separado; o limiter roda depois da autenticação).
            o.AddPolicy(PlanningQueueService.SimulatePolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "plan-sim|" + TenantKey(http) + "|" + ((http.GetEndpoint() as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText ?? "-") + "|"
                + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? http.Connection.RemoteIpAddress?.ToString() ?? "-"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = PlanningQueueService.SimulatePerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Política nomeada do quiosque (ADR 0030 §1): 10 trocas de token por minuto por IP e tenant; o 401 já tem tempo uniforme.
            o.AddPolicy(BemAqui.BuildingBlocks.Web.KioskEndpoints.SessionRateLimitPolicy, http =>
                RateLimitPartition.GetFixedWindowLimiter("kiosk|" + TenantKey(http) + "|" + (http.Connection.RemoteIpAddress?.ToString() ?? "-"), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.KioskSessionPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
            // "2FA de novo" (BNK-01a CA 1): 10 códigos a cada 5 min por usuário e tenant (o anti-replay do código já vale; isto corta a
            // força bruta dos 6 dígitos). "Testar conexão" do Inter: 6 por minuto por usuário e rota (chamada externa com mTLS).
            o.AddPolicy(BemAqui.BuildingBlocks.Web.RecentTwoFactorEndpoints.StepUpRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "step-up|" + TenantKey(http) + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? http.Connection.RemoteIpAddress?.ToString() ?? "-"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(5), QueueLimit = 0 }));
            o.AddPolicy(BemAqui.Finance.Application.BankIntegrationService.TestRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "bank-test|" + TenantKey(http) + "|" + (http.GetEndpoint()?.DisplayName ?? "-") + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? http.Connection.RemoteIpAddress?.ToString() ?? "-"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Webhooks (FND-16 CA 8, §4.3): política nomeada, por IP real (KnownProxies, D19) e provedor, antes de ler o corpo e de abrir o
            // banco do tenant. Provedor fora do registro conta como "-" (o código da URL não cria partição nova).
            var webhooks = configuration.GetSection(BemAqui.BuildingBlocks.Webhooks.WebhookOptions.Section).Get<BemAqui.BuildingBlocks.Webhooks.WebhookOptions>()
                ?? new BemAqui.BuildingBlocks.Webhooks.WebhookOptions();
            o.AddPolicy(BemAqui.BuildingBlocks.Webhooks.WebhookEndpoints.RateLimitPolicy, http =>
            {
                var code = http.Request.RouteValues["provider"] as string;
                var provider = http.RequestServices.GetRequiredService<BemAqui.BuildingBlocks.Webhooks.WebhookProviderRegistry>().TryGet(code, out var known) ? known.Code : "-";
                return RateLimitPartition.GetFixedWindowLimiter("webhook|" + provider + "|" + (http.Connection.RemoteIpAddress?.ToString() ?? "-"), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = Math.Max(1, webhooks.PerIpPerMinute),
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
            // "Testar conexão" do Emissor Fiscal (FIS-01 CA 18): política nomeada, por usuário dentro do tenant; cada teste chama o Emissor.
            var fiscal = configuration.GetSection(BemAqui.Fiscal.Application.FiscalIntegrationOptions.Section).Get<BemAqui.Fiscal.Application.FiscalIntegrationOptions>()
                ?? new BemAqui.Fiscal.Application.FiscalIntegrationOptions();
            o.AddPolicy(BemAqui.Fiscal.Application.FiscalIntegrationService.TestRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "fiscal-test|" + TenantKey(http) + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? http.Connection.RemoteIpAddress?.ToString() ?? "-"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, fiscal.TestsPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Envio do certificado A1 (FIS-02a CA 15): 5 por hora por tenant.
            o.AddPolicy(BemAqui.Fiscal.Application.Certificates.FiscalCertificateService.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "fiscal-certificate|" + TenantKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, fiscal.CertificateUploadsPerHour), Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
            // "Simular" das regras fiscais (FIS-02b CA 10): 30 por minuto por usuário dentro do tenant; nada é gravado nem vai ao Emissor.
            o.AddPolicy(BemAqui.Fiscal.Application.TaxRules.TaxSimulationService.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "fiscal-simulate|" + TenantKey(http) + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? http.Connection.RemoteIpAddress?.ToString() ?? "-"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, fiscal.SimulationsPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.OnRejected = async (context, cancellationToken) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.FromMinutes(1);
                context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                var simulate = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName == PlanningQueueService.SimulatePolicy;
                await Results.Problem(
                    statusCode: StatusCodes.Status429TooManyRequests,
                    title: simulate ? PlanningQueueService.SimulateLimitMessage : Message,
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
