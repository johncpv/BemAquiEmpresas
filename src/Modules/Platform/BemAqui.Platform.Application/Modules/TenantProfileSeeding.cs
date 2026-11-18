using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Platform.Application.Modules;

/// <summary>
/// Dados iniciais do perfil ao adicionar um perfil pela aba "Perfis" (PERF-02 e PERF-04): roda o <see cref="ProfileSeedRunner"/> no banco
/// do tenant logo depois de o perfil ser gravado. Idempotente (cada pacote cria só o que falta). Tenant ainda em provisionamento é pulado:
/// a etapa de dados iniciais do provisionamento é quem semeia. Falha no seed não desfaz o perfil: o <c>migrate</c> do próximo deploy roda
/// os pacotes de novo.
/// </summary>
public sealed partial class TenantProfileSeeding(ITenantResolver resolver, ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<TenantProfileSeeding> logger)
{
    public const string SeedFailedNotice = " Os dados iniciais do perfil não puderam ser criados agora; entram no próximo deploy.";

    /// <summary>Roda os pacotes do perfil no tenant. Devolve falso quando o seed falhou (o perfil continua aplicado).</summary>
    public async Task<bool> RunAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        resolver.Invalidate(); // o perfil acabou de mudar: o tenant resolvido precisa trazer os perfis e segmentos novos
        var tenant = await directory.FindAsync(tenantId, cancellationToken);
        if (tenant is null || tenant.Status == TenantStatus.Provisioning)
        {
            return true;
        }

        try
        {
            await using var scope = await scopes.CreateScopeAsync(tenant, cancellationToken);
            await scope.ServiceProvider.GetRequiredService<ProfileSeedRunner>().SeedAsync(scope.ServiceProvider, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSeedFailed(logger, tenantId, ex);
            return false;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Dados iniciais do perfil falharam no tenant {TenantId}.")]
    private static partial void LogSeedFailed(ILogger logger, Guid tenantId, Exception exception);
}
