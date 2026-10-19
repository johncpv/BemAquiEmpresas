using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Parties.Application;

public sealed record CnpjPurgeArgs;

/// <summary>Expurgo diário do cache de CNPJ por tenant (CAD-01b CA 14).</summary>
public sealed class CnpjLookupPurgeJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<CnpjLookupPurgeJob> logger)
    : TenantJob<CnpjPurgeArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, CnpjPurgeArgs args, CancellationToken cancellationToken) =>
        services.GetRequiredService<CnpjLookupService>().PurgeAsync(cancellationToken);
}

/// <summary>Recorrente global diário (03:00 de São Paulo) que enfileira o expurgo por tenant ativo.</summary>
public sealed class CnpjLookupPurgeRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<CnpjLookupPurgeJob, CnpjPurgeArgs>(directory, jobs)
{
    public const string Id = "cnpj-lookups-purge";
    public const string Cron = "0 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override CnpjPurgeArgs ArgsFor(TenantInfo tenant) => new();
}
