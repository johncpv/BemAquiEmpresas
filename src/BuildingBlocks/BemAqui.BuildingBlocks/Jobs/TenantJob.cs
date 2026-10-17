using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Jobs;

/// <summary>
/// Base obrigatória de todo job de tenant (ADR 0006). O único ponto de entrada recebe o tenantId,
/// então não há como agendar sem ele; o job abre um escopo novo do tenant e não roda se ele não estiver
/// ativo (FND-07 CA 15 e 16).
/// </summary>
public abstract partial class TenantJob<TArgs>(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger logger)
{
    public async Task ExecuteAsync(Guid tenantId, TArgs args, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Job de tenant sem tenantId.", nameof(tenantId));
        }

        var tenant = await directory.FindAsync(tenantId, cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active)
        {
            LogSkipped(logger, GetType().Name, tenantId, tenant?.Status);
            return;
        }

        await using var scope = await scopes.CreateScopeAsync(tenant, cancellationToken);
        await RunAsync(scope.ServiceProvider, tenant, args, cancellationToken);
    }

    /// <summary>Trabalho do job, já dentro do escopo do tenant.</summary>
    protected abstract Task RunAsync(IServiceProvider services, TenantInfo tenant, TArgs args, CancellationToken cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {Job} não executado para o tenant {TenantId}: tenant inexistente ou não ativo ({Status}).")]
    private static partial void LogSkipped(ILogger logger, string job, Guid tenantId, TenantStatus? status);
}
