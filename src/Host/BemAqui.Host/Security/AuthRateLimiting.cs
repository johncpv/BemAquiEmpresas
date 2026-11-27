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

    /// <summary>Entrada por PIN no caixa do PDV (PDV-01 CA 11, política nomeada <c>pos-pin-login</c>), por tenant, IP e aparelho.</summary>
    public int PosPinPerMinute { get; set; } = 10;

    /// <summary>Busca de cliente na tela de venda (PDV-02, política nomeada <c>pos-customer-search</c>), por tenant, usuário e aparelho.</summary>
    public int PosCustomerSearchPerMinute { get; set; } = 20;
}

/// <summary>
/// Limites dos aplicativos conectados (INT-01 CA 19, ADR 0053 §8). <c>Integrations:AppRateLimit</c> é o parâmetro de plataforma das rotas do
/// aplicativo (por conexão), compartilhado com a INT-03.
/// </summary>
public sealed class AppRateLimitOptions
{
    public const string Section = "Integrations:AppRateLimit";

    public int ReadsPerMinute { get; set; } = 600;

    public int WritesPerMinute { get; set; } = 120;

    /// <summary><c>/oauth/token</c> e <c>/oauth/revoke</c>, por IP e cliente.</summary>
    public int TokenPerMinute { get; set; } = 30;

    /// <summary>Conferir, autorizar e cancelar o consentimento, por usuário.</summary>
    public int ConsentPerMinute { get; set; } = 10;
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
            // Entrada por PIN no caixa do PDV (PDV-01 CA 11): por tenant, IP e aparelho (hash do cookie __Host-bq_pos; vários caixas atrás do mesmo IP da
            // loja não dividem o limite). O bloqueio do PIN (5 erros) e do aparelho (20 em 15 min) já cortam a força bruta; isto limita o custo do PBKDF2.
            // Cookie inventado não chega ao PBKDF2: o esquema Device recusa antes.
            o.AddPolicy(BemAqui.BuildingBlocks.Web.PinSessionEndpoints.PosPinLoginRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "pos-pin|" + TenantKey(http) + "|" + PosDeviceKey(http) + "|"
                + (http.Connection.RemoteIpAddress?.ToString() ?? "-"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = options.PosPinPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Subida dos eventos do caixa (OFF-02, revisão de segurança M3): por tenant e aparelho (hash do cookie), inclusive o aparelho revogado, que
            // continua com o endpoint aberto. O lote leva até 200 eventos; o caixa sobe a cada 20 s, então o limite só corta abuso.
            o.AddPolicy(BemAqui.Pos.Application.PosEventIngestion.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "pos-sync|" + TenantKey(http) + "|" + PosDeviceKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = BemAqui.Pos.Application.PosEventIngestion.RateLimitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Saldo da loja para o caixa (PDV-05, revisão de segurança B2): por tenant e aparelho, no mesmo padrão da subida dos eventos.
            o.AddPolicy(BemAqui.Pos.Application.PosStockSyncService.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "pos-stock|" + TenantKey(http) + "|" + PosDeviceKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = BemAqui.Pos.Application.PosStockSyncService.RateLimitPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Busca de cliente na tela de venda (PDV-02, revisão de segurança, achado 1): por tenant, usuário (o operador da sessão por PIN) e
            // aparelho (hash do cookie), contra a mineração da base de clientes pelo caixa.
            o.AddPolicy(BemAqui.Pos.Application.PosCustomerSearchService.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "pos-customer|" + TenantKey(http) + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? "-") + "|" + PosDeviceKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, options.PosCustomerSearchPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // PDV-06: localizar a venda (número ou CPF) e consultar ou usar o vale-troca (valor ao portador): por tenant, usuário e aparelho, como a busca de cliente.
            o.AddPolicy(BemAqui.Sales.Application.SaleReturnService.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "pos-returns|" + TenantKey(http) + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? "-") + "|" + PosDeviceKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, options.PosCustomerSearchPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // PDV-07 (revisão de segurança): revelar o CPF/CNPJ inteiro na retaguarda, por tenant e usuário (~20 por minuto), contra a coleta em massa.
            o.AddPolicy(BemAqui.Sales.Application.SaleBackofficeService.RevealRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "sales-reveal|" + TenantKey(http) + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? "-"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, options.PosCustomerSearchPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(BemAqui.Sales.Application.StoreCreditService.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "pos-credits|" + TenantKey(http) + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? "-") + "|" + PosDeviceKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, options.PosCustomerSearchPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // PIN do supervisor no caixa (PDV-04 CA 10 e 12): por tenant e aparelho, como a entrada por PIN; o bloqueio do PIN e do aparelho corta a força bruta,
            // isto limita o custo do PBKDF2.
            o.AddPolicy(BemAqui.Pos.Application.SupervisorApprovalService.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "pos-supervisor|" + TenantKey(http) + "|" + PosDeviceKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = options.PosPinPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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
            // "Importar XML" da nota de entrada (FIS-04a CA 1): 60 arquivos por hora por usuário dentro do tenant (um arquivo por requisição).
            o.AddPolicy(BemAqui.Fiscal.Application.Incoming.IncomingInvoiceService.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "fiscal-incoming|" + TenantKey(http) + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? http.Connection.RemoteIpAddress?.ToString() ?? "-"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, fiscal.IncomingImportsPerHour), Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
            // "Importar PDF da DANFE" (FIS-05 CA 1): 30 PDFs por hora por usuário dentro do tenant (cada um vai ao worker isolado).
            o.AddPolicy(BemAqui.Fiscal.Domain.IncomingReadingRules.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "fiscal-pdf|" + TenantKey(http) + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? http.Connection.RemoteIpAddress?.ToString() ?? "-"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, fiscal.PdfReadingsPerHour), Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
            // Aplicativos conectados (INT-01 CA 19, ADR 0053 §8): /oauth/token e /oauth/revoke por IP e cliente (30/min); conferir e autorizar o
            // consentimento por usuário (10/min); as rotas do aplicativo por conexão, leituras e escritas separadas (parâmetro Integrations:AppRateLimit).
            var appLimits = configuration.GetSection(AppRateLimitOptions.Section).Get<AppRateLimitOptions>() ?? new AppRateLimitOptions();
            o.AddPolicy(BemAqui.BuildingBlocks.Web.ConnectedAppEndpoints.TokenRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "oauth-token|" + TenantKey(http) + "|" + (http.Connection.RemoteIpAddress?.ToString() ?? "-") + "|" + ClientKey(http),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, appLimits.TokenPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(BemAqui.BuildingBlocks.Web.ConnectedAppEndpoints.ConsentRateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
                "oauth-consent|" + TenantKey(http) + "|" + (http.RequestServices.GetService<ICurrentUser>()?.UserId?.ToString() ?? http.Connection.RemoteIpAddress?.ToString() ?? "-"),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, appLimits.ConsentPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy(BemAqui.BuildingBlocks.Web.ConnectedAppEndpoints.RateLimitPolicy, http =>
            {
                var write = !HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method);
                var connection = BemAqui.BuildingBlocks.Security.AppClaims.ConnectionOf(http.User)?.ToString() ?? "ip:" + (http.Connection.RemoteIpAddress?.ToString() ?? "-");
                return RateLimitPartition.GetFixedWindowLimiter("app-api|" + TenantKey(http) + "|" + connection + "|" + (write ? "w" : "r"),
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, write ? appLimits.WritesPerMinute : appLimits.ReadsPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
            });
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

    /// <summary>Cliente OAuth pelo <c>client_id</c> do <c>Authorization: Basic</c> (nunca o segredo na chave da partição).</summary>
    private static string ClientKey(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) || header.Length > 512)
        {
            return "-";
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(header[6..].Trim()));
            var separator = raw.IndexOf(':', StringComparison.Ordinal);
            return separator > 0 && separator <= 64 ? raw[..separator] : "-";
        }
        catch (FormatException)
        {
            return "-";
        }
    }

    /// <summary>Aparelho de caixa pelo hash do cookie (nunca o segredo em si na chave da partição).</summary>
    private static string PosDeviceKey(HttpContext http) =>
        http.Request.Cookies[BemAqui.BuildingBlocks.Security.PosDeviceCookie.Name] is { Length: > 0 and <= 128 } secret
            ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)))[..16]
            : "-";
}
