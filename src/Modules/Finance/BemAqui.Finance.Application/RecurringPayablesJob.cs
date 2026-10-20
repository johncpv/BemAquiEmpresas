using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Finance.Application;

public sealed record RecurringPayablesArgs;

/// <summary>Gera os títulos das despesas recorrentes de um tenant (FIN-02 CA 17 a 19); argumentos só de ids.</summary>
public sealed class GenerateRecurringPayablesJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<GenerateRecurringPayablesJob> logger)
    : TenantJob<RecurringPayablesArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, RecurringPayablesArgs args, CancellationToken cancellationToken) =>
        services.GetRequiredService<RecurringExpenseService>().GenerateAsync(cancellationToken);
}

/// <summary>Recorrente global diário (06:00 de São Paulo) que enfileira a geração por tenant ativo.</summary>
public sealed class RecurringPayablesRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<GenerateRecurringPayablesJob, RecurringPayablesArgs>(directory, jobs)
{
    public const string Id = "finance-recurring-payables";
    public const string Cron = "0 6 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override RecurringPayablesArgs ArgsFor(TenantInfo tenant) => new();
}
