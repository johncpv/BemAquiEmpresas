using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.HR.Application;

/// <summary>
/// Eventos dos afastamentos com data futura (CAD-04c1 CA 7): no dia do início publica o <c>EmployeeLeaveStarted</c>; no dia seguinte
/// ao fim previsto, o <c>EmployeeLeaveEnded</c>. Só publica: a situação "Afastado" é calculada na leitura. Idempotente pelas marcas.
/// </summary>
public sealed class EmployeeLeaveEvents(IHrDb db, IEventPublisher events, TimeProvider clock)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var now = clock.GetUtcNow();
        var due = await db.EmployeeHistory
            // Cancelado não publica (CAD-04c1b); o encerrado antes do previsto já publicou o fim no próprio encerramento.
            .Where(h => h.Kind == EmployeeHistoryKind.Leave && h.CancelledAt == null
                && ((h.StartPublishedAt == null && h.EffectiveDate <= today) || (h.EndPublishedAt == null && h.LeaveEndDate < today)))
            .ToListAsync(cancellationToken);
        var published = due.Count(leave => LeaveEvents.PublishDue(events, leave, today, now));
        await db.SaveChangesAsync(cancellationToken);
        return published;
    }
}

public sealed record EmployeeLeaveEventsArgs;

/// <summary>Execução por tenant (recebe o <c>tenantId</c>); pula o tenant com o módulo <c>hr</c> não liberado.</summary>
public sealed partial class EmployeeLeaveEventsJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<EmployeeLeaveEventsJob> logger)
    : TenantJob<EmployeeLeaveEventsArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, EmployeeLeaveEventsArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenant);
        if (services.GetRequiredService<TenantContextHolder>().Resolved?.IsReleased("hr") != true)
        {
            var log = services.GetRequiredService<ILogger<EmployeeLeaveEventsJob>>();
            LogNotReleased(log, tenant.Id);
            return Task.CompletedTask;
        }

        return services.GetRequiredService<EmployeeLeaveEvents>().RunAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Eventos de afastamento não executados para o tenant {TenantId}: módulo hr não liberado.")]
    private static partial void LogNotReleased(ILogger logger, Guid tenantId);
}

/// <summary>
/// Recorrente global diário (00:15 de São Paulo, horário inicial; muda pelo parâmetro <c>Jobs:HrLeaveEvents:Cron</c>) que enfileira
/// os eventos de afastamento por tenant ativo.
/// </summary>
public sealed class EmployeeLeaveEventsRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<EmployeeLeaveEventsJob, EmployeeLeaveEventsArgs>(directory, jobs)
{
    public const string Id = "hr-leave-events";
    public const string Cron = "15 0 * * *";
    public const string CronSetting = "Jobs:HrLeaveEvents:Cron";

    protected override string Queue => JobQueues.Maintenance;

    protected override EmployeeLeaveEventsArgs ArgsFor(TenantInfo tenant) => new();
}
