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
}

/// <summary>
/// Liga cada requisição ao tenant pelo host (ADR 0004). Host desconhecido → 404 sem revelar nada;
/// host da plataforma não tem rotas de tenant; suspenso → 403 <c>tenant_suspended</c> (FND-02 CA 2, 3 e 5).
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next, ITenantResolver resolver, Microsoft.Extensions.Options.IOptions<PlatformHostOptions> platform)
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
        if (string.Equals(host, TenantDomain.Normalize(platform.Value.Host), StringComparison.Ordinal))
        {
            // Rotas da plataforma chegam nas FND-13/03b; rota de tenant aqui não existe.
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
