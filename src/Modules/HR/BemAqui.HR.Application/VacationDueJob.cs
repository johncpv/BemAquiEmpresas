using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.HR.Application;

public sealed record VacationDueArgs;

/// <summary>
/// Férias do dia por tenant (FLH-03 CA 3): recebe o <c>tenantId</c>; refaz os períodos, marca as gozadas e avisa os vencimentos (uma vez por
/// período, <c>vacation-due:{periodId}</c>). Pula o tenant com o módulo <c>hr</c> não liberado.
/// </summary>
public sealed partial class VacationDueJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<VacationDueJob> logger)
    : TenantJob<VacationDueArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, VacationDueArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenant);
        if (services.GetRequiredService<TenantContextHolder>().Resolved?.IsReleased("hr") != true)
        {
            var log = services.GetRequiredService<ILogger<VacationDueJob>>();
            LogNotReleased(log, tenant.Id);
            return Task.CompletedTask;
        }

        return services.GetRequiredService<VacationService>().DailyAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Férias do dia não executadas para o tenant {TenantId}: módulo hr não liberado.")]
    private static partial void LogNotReleased(ILogger logger, Guid tenantId);
}

/// <summary>Recorrente global diário (06:20 de São Paulo, horário inicial; muda por <c>Jobs:HrVacationDue:Cron</c>) que enfileira um job por tenant.</summary>
public sealed class VacationDueRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<VacationDueJob, VacationDueArgs>(directory, jobs)
{
    public const string Id = "hr-vacation-due";
    public const string Cron = "20 6 * * *";
    public const string CronSetting = "Jobs:HrVacationDue:Cron";

    protected override string Queue => JobQueues.Maintenance;

    protected override VacationDueArgs ArgsFor(TenantInfo tenant) => new();
}
