using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Production.Contracts;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Production.Application;

/// <summary>Argumentos do recálculo por tenant (PLN-01a CA 13): só o gatilho, sem id nem dado pessoal; o tenant vai no <c>tenantId</c> do job.</summary>
public sealed record PlanningRecalculateArgs(ScheduleTrigger Trigger = ScheduleTrigger.Hourly);

/// <summary>
/// Recálculo do plano no tenant (PLN-01a CA 13): confere de novo, no início, o módulo <c>production</c> liberado (sem ele, termina sem
/// calcular) e chama o escritor único. Idempotente: rodar duas vezes substitui o plano pelo mesmo resultado, sem duplicar linhas.
/// </summary>
public sealed class PlanningRecalculateJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<PlanningRecalculateJob> logger)
    : TenantJob<PlanningRecalculateArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, PlanningRecalculateArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (services.GetRequiredService<TenantContextHolder>().Resolved?.IsReleased(PlanningService.Module) != true)
        {
            return;
        }

        await services.GetRequiredService<ScheduleService>().RecalculateAsync(args.Trigger, null, cancellationToken);
    }
}

/// <summary>
/// Recorrente global <c>planning-recalculate</c> de hora em hora (fuso de São Paulo, no <c>RecurringJobsRegistrar</c>): enfileira um
/// <see cref="PlanningRecalculateJob"/> por tenant ativo com o módulo <c>production</c> liberado, só com o <c>tenantId</c>.
/// </summary>
public sealed class PlanningRecalculateRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs, ITenantResolver resolver)
    : GlobalRecurringJob<PlanningRecalculateJob, PlanningRecalculateArgs>(directory, jobs)
{
    public const string Id = "planning-recalculate";
    public const string Cron = "0 * * * *";

    protected override string Queue => JobQueues.Default;

    protected override PlanningRecalculateArgs ArgsFor(TenantInfo tenant) => new();

    protected override async Task<bool> ShouldEnqueueAsync(TenantInfo tenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return (await resolver.FindByIdAsync(tenant.Id, cancellationToken))?.IsReleased(PlanningService.Module) == true;
    }
}
