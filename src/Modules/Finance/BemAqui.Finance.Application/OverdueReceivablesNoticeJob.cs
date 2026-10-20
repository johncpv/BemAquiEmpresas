using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Finance.Application;

public sealed record OverdueNoticeArgs;

/// <summary>Aviso dos títulos a receber que venceram ontem num tenant (FIN-06a CA 4); argumentos só de ids.</summary>
public sealed class OverdueReceivablesNoticeJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<OverdueReceivablesNoticeJob> logger)
    : TenantJob<OverdueNoticeArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, OverdueNoticeArgs args, CancellationToken cancellationToken) =>
        services.GetRequiredService<AgingService>().NotifyOverdueAsync(cancellationToken);
}

/// <summary>Recorrente global diário (08:00 de São Paulo) que enfileira o aviso por tenant ativo.</summary>
public sealed class OverdueReceivablesRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<OverdueReceivablesNoticeJob, OverdueNoticeArgs>(directory, jobs)
{
    public const string Id = "finance-overdue-receivables";
    public const string Cron = "0 8 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override OverdueNoticeArgs ArgsFor(TenantInfo tenant) => new();
}
