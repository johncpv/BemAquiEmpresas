using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Hangfire;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>O que resolve o "Sem previsão" (PLN-01a, propositivo): escolher roteiro, informar o centro da etapa ou revisar o calendário.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<PlanningWarningAction>))]
public enum PlanningWarningAction
{
    ChooseRouting,
    SetWorkCenter,
    ReviewCalendar,
}

/// <summary>OS sem previsão no cabeçalho do Planejamento, com o motivo e a ação que resolve.</summary>
public sealed record PlanningWarning(Guid OrderId, string OrderNumber, string Reason, PlanningWarningAction Action);

/// <summary>
/// Cabeçalho do Planejamento (PLN-01a, Telas e estados): a última execução (com sucesso ou erro), a última bem-sucedida (as datas
/// mostradas), se há recálculo pendente, quantas OS de fabricação estão abertas e os avisos de OS sem previsão.
/// </summary>
public sealed record PlanningSummary(ScheduleRunInfo? LastRun, ScheduleRunInfo? LastSuccess, bool Pending, int OpenOrders, IReadOnlyList<PlanningWarning> Warnings);

/// <summary>
/// Telas do Planejamento (PLN-01a CA 14, 16 e 18): o cabeçalho, as previsões das OS para a aba Etapas e o Kanban, e o "Recalcular agora",
/// que roda no request ou, com mais OS do que cabe em 5 s, vai para a fila do tenant.
/// </summary>
public sealed class PlanningService(
    IProductionDb db, IProductionSchedule schedule, ScheduleService writer, ICurrentUser currentUser, TenantContextHolder tenant, IBackgroundJobClient jobs)
{
    public const string Module = "production";

    /// <summary>Acima disto o recálculo manual vai para a fila: 300 OS cabem em menos de 5 s (CA 17), com folga.</summary>
    public const int InRequestLimit = 400;

    private const int MaxWarnings = 100;

    private static readonly ProductionOrderStatus[] OpenStatuses =
        [ProductionOrderStatus.Open, ProductionOrderStatus.InProgress, ProductionOrderStatus.Paused, ProductionOrderStatus.Quality, ProductionOrderStatus.ReadyToComplete];

    public async Task<PlanningSummary> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var open = db.ProductionOrders.AsNoTracking().Where(o => o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status));
        var openCount = await open.CountAsync(cancellationToken);
        var lastRun = await schedule.LastRunAsync(cancellationToken);
        var lastSuccess = lastRun?.Status == ScheduleRunStatus.Succeeded ? lastRun : await db.ScheduleRunsView.Where(r => r.Status == ScheduleRunStatus.Succeeded)
            .OrderByDescending(r => r.FinishedAt).ThenByDescending(r => r.Id).Select(r => ScheduleService.Info(r)).FirstOrDefaultAsync(cancellationToken);
        var pending = await db.ScheduleRunsView.AnyAsync(r => r.Status == ScheduleRunStatus.Pending, cancellationToken);

        // OS sem etapas ("Sem roteiro", CA 9) não entram no cálculo, mas aparecem no aviso com "Escolher roteiro".
        var withoutSteps = await open.Where(o => !db.ProductionOrderSteps.Any(s => s.OrderId == o.Id)).OrderBy(o => o.Number).Take(MaxWarnings)
            .Select(o => new { o.Id, o.Number }).ToListAsync(cancellationToken);
        var blocked = await db.ScheduleStepForecastsView.Where(f => f.NoForecastReason != null)
            .Join(open, f => f.ProductionOrderId, o => o.Id, (f, o) => new { o.Id, o.Number, f.Sequence, Reason = f.NoForecastReason! })
            .ToListAsync(cancellationToken);
        var warnings = withoutSteps.Select(o => new PlanningWarning(o.Id, o.Number, "OS sem etapas (sem roteiro)", PlanningWarningAction.ChooseRouting))
            .Concat(blocked.GroupBy(b => (b.Id, b.Number)).OrderBy(g => g.Key.Number, StringComparer.Ordinal)
                .Select(g => g.OrderBy(b => b.Sequence).First()).Select(b => new PlanningWarning(b.Id, b.Number, b.Reason, ActionFor(b.Reason))))
            .Take(MaxWarnings).ToList();
        return new PlanningSummary(lastRun, lastSuccess, pending, openCount, warnings);
    }

    public Task<IReadOnlyList<OrderForecastInfo>> ForOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default) =>
        schedule.ForOrdersAsync(orderIds, cancellationToken);

    /// <summary>"Recalcular agora" (CA 14): no request, ou na fila do tenant quando há OS demais para caber em 5 s.</summary>
    public async Task<ScheduleRecalculation> RecalculateNowAsync(CancellationToken cancellationToken = default)
    {
        var count = await db.ProductionOrders.AsNoTracking().CountAsync(o => o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status), cancellationToken);
        if (count > InRequestLimit && tenant.Resolved?.Tenant.Id is { } tenantId)
        {
            var args = new PlanningRecalculateArgs(ScheduleTrigger.Manual);
            jobs.Create<PlanningRecalculateJob>(j => j.ExecuteAsync(tenantId, args, CancellationToken.None), new EnqueuedState(JobQueues.Default));
            return new ScheduleRecalculation(false, true, null);
        }

        return await writer.RecalculateAsync(ScheduleTrigger.Manual, currentUser.UserId, cancellationToken);
    }

    /// <summary>O motivo diz a ação: etapa sem centro → informar o centro na aba Etapas; centro sem capacidade → revisar o calendário.</summary>
    public static PlanningWarningAction ActionFor(string reason) =>
        reason.EndsWith("sem centro de trabalho", StringComparison.Ordinal) ? PlanningWarningAction.SetWorkCenter : PlanningWarningAction.ReviewCalendar;
}
