using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Hangfire.States;

namespace BemAqui.BuildingBlocks.Jobs;

/// <summary>
/// Um recorrente global por tipo de rotina (ADR 0006): ao disparar, enfileira um <typeparamref name="TJob"/>
/// por tenant ativo, na fila <see cref="Queue"/>. Não existe recorrente por tenant.
/// </summary>
public abstract class GlobalRecurringJob<TJob, TArgs>(ITenantDirectory directory, IBackgroundJobClient jobs)
    where TJob : TenantJob<TArgs>
{
    /// <summary>Fila das execuções por tenant (ex.: <see cref="JobQueues.Default"/>, <see cref="JobQueues.Email"/>).</summary>
    protected abstract string Queue { get; }

    /// <summary>Argumentos da execução de cada tenant.</summary>
    protected abstract TArgs ArgsFor(TenantInfo tenant);

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var tenants = await directory.GetActiveAsync(cancellationToken);
        var enqueued = 0;
        foreach (var tenant in tenants.Where(t => t.Status == TenantStatus.Active))
        {
            var args = ArgsFor(tenant);
            var tenantId = tenant.Id;
            jobs.Create<TJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), new EnqueuedState(Queue));
            enqueued++;
        }

        return enqueued;
    }
}

/// <summary>Filas separadas, com poucos workers (VPS compartilhada).</summary>
public static class JobQueues
{
    public const string Default = "default";
    public const string Outbox = "outbox";
    public const string Email = "email";
    public const string Fiscal = "fiscal";
    public const string Maintenance = "maintenance";

    public static readonly string[] All = [Default, Outbox, Email, Fiscal, Maintenance];
}
