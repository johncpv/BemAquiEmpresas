using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Host.Persistence;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Host.Tenancy;

/// <summary>Seção <c>Platform</c>: host da plataforma, fora de <c>tenant_domains</c> (ADR 0005 D2).</summary>
public sealed class PlatformHostOptions
{
    public const string Section = "Platform";

    public string Host { get; set; } = "plataforma.bemaquiapp.com.br";

    /// <summary>
    /// Allowlist opcional de redes (CIDR) do host da plataforma, além da do nginx (ADR 0010 item 7). Vazia: a app
    /// não filtra. Fora dela: 404, antes de chegar ao login (FND-13 CA 2).
    /// </summary>
    public string[] AllowedNetworks { get; set; } = [];

    /// <summary>Prefixos que só existem no host da plataforma (FND-13 CA 5).</summary>
    public static readonly string[] PlatformPaths = ["/api/v1/platform", "/hangfire"];

    public static bool IsPlatformPath(PathString path) =>
        PlatformPaths.Any(p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    public bool IsAllowed(System.Net.IPAddress? address)
    {
        if (AllowedNetworks.Length == 0)
        {
            return true;
        }

        if (address is null)
        {
            return false;
        }

        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return AllowedNetworks.Any(n => System.Net.IPNetwork.TryParse(n, out var network) && network.Contains(ip));
    }
}

/// <summary>
/// Liga cada requisição ao tenant pelo host (ADR 0004). Host desconhecido → 404 sem revelar nada;
/// host da plataforma não tem rotas de tenant; suspenso → 403 <c>tenant_suspended</c> (FND-02 CA 2, 3 e 5).
/// </summary>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    ITenantResolver resolver,
    Microsoft.Extensions.Options.IOptions<PlatformHostOptions> platform)
{
    private const string PublicContextPath = "/api/v1/public/tenant-context";

    public async Task InvokeAsync(HttpContext context, TenantContextHolder holder)
    {
        ArgumentNullException.ThrowIfNull(context);
        // /health para o healthcheck da infra; /openapi só existe em Development (geração do cliente pelo Orval).
        if (context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            || context.Request.Path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var host = TenantDomain.Normalize(context.Request.Host.Host);

        // Webhooks (FND-16 CA 1, ADR 0031 §1): fora do tenant por host e fora da allowlist de IP do painel; só no host da plataforma
        // (o EmissorFiscal também, pela URL pública: ADR 0027, adendo de 27/10/2026). O tenant vem do tenantKey no endpoint; em host de tenant a rota não existe.
        if (context.Request.Path.StartsWithSegments(BemAqui.BuildingBlocks.Webhooks.WebhookEndpoints.PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(host, TenantDomain.Normalize(platform.Value.Host), StringComparison.Ordinal))
            {
                await next(context);
                return;
            }

            await NotFound(context);
            return;
        }
        if (string.Equals(host, TenantDomain.Normalize(platform.Value.Host), StringComparison.Ordinal))
        {
            // Host da plataforma: só as rotas da plataforma, e só de IP permitido. Rota de tenant aqui não existe.
            if (platform.Value.IsAllowed(context.Connection.RemoteIpAddress) && PlatformHostOptions.IsPlatformPath(context.Request.Path))
            {
                await next(context);
                return;
            }

            await NotFound(context);
            return;
        }

        if (PlatformHostOptions.IsPlatformPath(context.Request.Path))
        {
            await NotFound(context);
            return;
        }

        var tenant = await resolver.ResolveByHostAsync(host, context.RequestAborted);
        if (tenant is null)
        {
            await NotFound(context);
            return;
        }

        var publicContext = context.Request.Path.StartsWithSegments(PublicContextPath, StringComparison.OrdinalIgnoreCase);
        if (tenant.Tenant.Status == TenantStatus.Suspended && !publicContext)
        {
            await Error.Forbidden(Error.Codes.TenantSuspended, "O acesso desta empresa ao BemAqui Empresas está suspenso. Fale com o responsável pela conta.")
                .ToProblem().ExecuteAsync(context);
            return;
        }

        if (tenant.Tenant.Status == TenantStatus.Provisioning && !publicContext)
        {
            await Error.Unavailable("tenant_provisioning", "Este ambiente ainda está sendo preparado. Tente de novo em alguns minutos.")
                .ToProblem().ExecuteAsync(context);
            return;
        }

        // Schema abaixo do que este build espera: 503 até o migrator rodar (FND-03a CA 12). Schema mais novo é
        // aceito (expand/contract: a versão anterior da API continua compatível).
        if (!publicContext && string.CompareOrdinal(tenant.SchemaVersion ?? string.Empty, TenantSchema.ExpectedVersion) < 0)
        {
            await Error.Unavailable(Error.Codes.TenantSchemaOutdated, "O sistema está sendo atualizado. Tente de novo em alguns minutos.")
                .ToProblem().ExecuteAsync(context);
            return;
        }

        holder.Set(tenant);
        await next(context);
    }

    private static Task NotFound(HttpContext context) =>
        Error.NotFound(Error.Codes.NotFound, "Endereço não encontrado.").ToProblem().ExecuteAsync(context);
}
