using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Production.Application;

/// <summary>Resultado do pedido de recálculo: <c>Ran</c> com a execução (sucesso ou erro) ou <c>Queued</c> (marcado "pendente" ou na fila).</summary>
public sealed record ScheduleRecalculation(bool Ran, bool Queued, ScheduleRunInfo? Run);

/// <summary>
/// Único escritor do plano (PLN-01a CA 10 a 12, ADR 0029 §2): monta a entrada, roda o <see cref="FiniteScheduler"/> e, numa transação só,
/// substitui <c>schedule_step_forecasts</c> e <c>schedule_allocations</c>, grava a previsão de cada projeto que mudou em
/// <c>schedule_project_forecasts</c> com o <c>ProjectForecastChanged</c> no outbox (PLN-01b CA 7) e grava a execução em
/// <c>schedule_runs</c>. Nunca toca <c>production_orders</c> nem <c>production_order_steps</c>; as tabelas do plano ficam fora da auditoria.
/// <para>
/// Advisory lock por tenant (<c>pg_try_advisory_xact_lock</c>) em duas transações: a primeira, curta, <b>assume</b> a marca "pendente"
/// (apaga a linha <c>Pending</c> e grava, para que o pedido seguinte marque de novo); a segunda calcula e grava. Quem chega durante o
/// cálculo marca "pendente" (no máximo uma, índice único parcial) e quem termina roda de novo enquanto houver marca: no máximo 1 rodando e
/// 1 pendente, nunca dois em paralelo (PLN-01b CA 3). Execução com erro mantém o plano anterior e registra o erro. Depois do commit avisa o
/// Kanban (<c>OrderBoardChanged</c> só das OS cujas datas ou cujo projeto mudaram) e manda o sinal <c>ScheduleChanged</c> (CA 4).
/// </para>
/// </summary>
public sealed partial class ScheduleService(
    IProductionPlanDb plan, ScheduleInputBuilder builder, ProjectForecastBuilder projects, PlanningQueueStore queue, IEventPublisher events, IProductionBoardPush push,
    ITenantContext tenant,
    TimeProvider clock, ILogger<ScheduleService> logger)
{
    /// <summary>Chave do advisory lock; cada tenant tem banco próprio, então a chave fixa já é por tenant.</summary>
    public const string LockKey = "production.schedule";

    public const string PendingIndex = "ux_schedule_runs_pending";

    /// <summary>Limite de execuções seguidas para as marcas que chegam durante o cálculo; o que sobrar fica para o próximo pedido ou para a hora cheia.</summary>
    public const int MaxFollowUps = 20;

    private const int MaxError = 2000;

    private const int MaxReason = 200;

    private enum Attempt
    {
        Ran,
        Busy,
        NothingPending,
    }

    private sealed record Written(IReadOnlyCollection<Guid> ChangedOrders);

    public async Task<ScheduleRecalculation> RecalculateAsync(ScheduleTrigger trigger, Guid? requestedBy, CancellationToken cancellationToken = default)
    {
        var (state, run) = await TryRunAsync(trigger, requestedBy, onlyIfPending: false, cancellationToken);
        if (state == Attempt.Busy)
        {
            await MarkPendingAsync(trigger, requestedBy, cancellationToken);

            // Quem está calculando pode ter terminado entre a tentativa e a marca: tenta de novo, só se a marca ainda está lá.
            var (retried, retryRun) = await TryRunAsync(trigger, requestedBy, onlyIfPending: true, cancellationToken);
            if (retried == Attempt.Ran)
            {
                await FollowUpsAsync(trigger, cancellationToken);
                return new ScheduleRecalculation(true, false, retryRun);
            }

            return new ScheduleRecalculation(false, true, null);
        }

        await FollowUpsAsync(trigger, cancellationToken);
        return new ScheduleRecalculation(true, false, run);
    }

    /// <summary>
    /// Recálculo pedido por evento (PLN-01b CA 1 a 3): roda só se a marca "pendente" ainda está lá (outro cálculo pode já tê-la assumido) e,
    /// com o cálculo de outro em andamento, deixa a marca para ele.
    /// </summary>
    public async Task<ScheduleRecalculation> RunPendingAsync(CancellationToken cancellationToken = default)
    {
        var (state, run) = await TryRunAsync(ScheduleTrigger.Event, null, onlyIfPending: true, cancellationToken);
        if (state != Attempt.Ran)
        {
            return new ScheduleRecalculation(false, state == Attempt.Busy, null);
        }

        await FollowUpsAsync(ScheduleTrigger.Event, cancellationToken);
        return new ScheduleRecalculation(true, false, run);
    }

    /// <summary>
    /// Marca "pendente" do pedido por evento (PLN-01b CA 3): true quando a marca nasceu agora (quem pediu enfileira o job); false quando já
    /// havia uma (o pedido se junta a ela: o job dela ou quem está calculando vai assumi-la).
    /// </summary>
    public async Task<bool> TryMarkPendingAsync(ScheduleTrigger trigger, CancellationToken cancellationToken = default) =>
        await MarkPendingAsync(trigger, null, cancellationToken);

    /// <summary>
    /// As marcas que chegaram durante o cálculo rodam em seguida, uma de cada vez (a marca é uma só, por mais pedidos que cheguem). No
    /// "Recalcular agora" roda no máximo uma, para o request não esperar a fábrica inteira; as outras ficam com o job delas.
    /// </summary>
    private async Task FollowUpsAsync(ScheduleTrigger trigger, CancellationToken ct)
    {
        var limit = trigger == ScheduleTrigger.Manual ? 1 : MaxFollowUps;
        for (var i = 0; i < limit; i++)
        {
            var (state, _) = await TryRunAsync(trigger, null, onlyIfPending: true, ct);
            if (state != Attempt.Ran)
            {
                return;
            }
        }
    }

    private async Task<(Attempt State, ScheduleRunInfo? Run)> TryRunAsync(ScheduleTrigger trigger, Guid? requestedBy, bool onlyIfPending, CancellationToken ct)
    {
        // 1) Assume a marca "pendente", sob o lock, numa transação curta: o pedido que chegar daqui em diante marca de novo.
        plan.ChangeTracker.Clear();
        ScheduleRun? claimed;
        await using (var claim = await plan.Database.BeginTransactionAsync(ct))
        {
            if (!await LockAsync(ct))
            {
                await claim.RollbackAsync(ct);
                return (Attempt.Busy, null);
            }

            claimed = await plan.ScheduleRuns.AsNoTracking().Where(r => r.Status == ScheduleRunStatus.Pending).OrderBy(r => r.RequestedAt).FirstOrDefaultAsync(ct);
            if (onlyIfPending && claimed is null)
            {
                await claim.CommitAsync(ct);
                return (Attempt.NothingPending, null);
            }

            if (claimed is not null)
            {
                await plan.ScheduleRuns.Where(r => r.Id == claimed.Id).ExecuteDeleteAsync(ct);
            }

            await claim.CommitAsync(ct);
        }

        // 2) Calcula e grava sob o lock. Se outro começou entre as duas transações, ele leu os dados depois do pedido assumido: cobre-o.
        var started = clock.GetTimestamp();
        var now = clock.GetUtcNow();
        var run = new ScheduleRun
        {
            Trigger = claimed?.Trigger ?? trigger, RequestedAt = claimed?.RequestedAt ?? now, RequestedBy = claimed?.RequestedBy ?? requestedBy, StartedAt = now,
        };
        Written written;
        await using (var transaction = await plan.Database.BeginTransactionAsync(ct))
        {
            if (!await LockAsync(ct))
            {
                await transaction.RollbackAsync(ct);
                return (Attempt.Busy, null);
            }

            try
            {
                written = await WriteAsync(run, now, ct);
                run.Status = ScheduleRunStatus.Succeeded;
                run.FinishedAt = clock.GetUtcNow();
                run.DurationMs = (int)clock.GetElapsedTime(started).TotalMilliseconds;
                plan.ScheduleRuns.Add(run);
                await plan.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                LogRun(logger, run.Trigger, run.OrdersCount, run.DurationMs);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, run.Trigger);
                await transaction.RollbackAsync(CancellationToken.None);
                run.Error = Truncate(ex.Message);
                written = new Written([]);
            }
        }

        if (run.Status != ScheduleRunStatus.Succeeded)
        {
            // Execução com erro: o plano anterior fica inteiro (a transação voltou) e o erro vai para schedule_runs (alerta de job, FND-12).
            plan.ChangeTracker.Clear();
            var failed = plan.ScheduleRuns.Add(new ScheduleRun
            {
                Trigger = run.Trigger, RequestedAt = run.RequestedAt, RequestedBy = run.RequestedBy, Status = ScheduleRunStatus.Failed, StartedAt = run.StartedAt,
                FinishedAt = clock.GetUtcNow(), DurationMs = (int)clock.GetElapsedTime(started).TotalMilliseconds, Error = run.Error,
            }).Entity;
            await plan.SaveChangesAsync(CancellationToken.None);
            return (Attempt.Ran, Info(failed));
        }

        await NotifyAsync(written.ChangedOrders, ct);
        return (Attempt.Ran, Info(run));
    }

    private async Task<bool> LockAsync(CancellationToken ct) =>
        await plan.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({LockKey}, 0)) AS \"Value\"").SingleAsync(ct);

    /// <summary>
    /// Calcula e substitui o plano (dentro da transação do lock): as linhas antigas saem e entram as novas, com a contagem de OS mudadas;
    /// a previsão de cada projeto que mudou é gravada e publicada (CA 7). Devolve as OS a avisar no Kanban.
    /// </summary>
    private async Task<Written> WriteAsync(ScheduleRun run, DateTimeOffset now, CancellationToken ct)
    {
        // PLN-02: a fila é mantida (entrada pela data prometida, saída das concluídas) na mesma transação e o motor segue a posição dela.
        var queueOrder = await queue.SyncAsync(ct);
        var input = await builder.BuildAsync(queueOrder, ct);
        var result = FiniteScheduler.Run(input);
        var previous = ScheduleViews.ByOrder(await plan.ScheduleStepForecasts.AsNoTracking()
            .Select(f => new ScheduleViews.Row(f.StepId, f.ProductionOrderId, f.Sequence, f.Hours, f.PlannedStart, f.PlannedEnd, f.NoForecastReason)).ToListAsync(ct));

        await plan.ScheduleAllocations.ExecuteDeleteAsync(ct);
        await plan.ScheduleStepForecasts.ExecuteDeleteAsync(ct);
        plan.ScheduleStepForecasts.AddRange(result.Steps.Select(s => new ScheduleStepForecast
        {
            StepId = s.StepId, ProductionOrderId = s.OrderId, Sequence = s.Sequence, Hours = s.Hours, PlannedStart = s.Start, PlannedEnd = s.End,
            NoForecastReason = s.NoForecastReason is { Length: > MaxReason } reason ? reason[..MaxReason] : s.NoForecastReason, RunId = run.Id,
        }));
        plan.ScheduleAllocations.AddRange(result.Allocations.Select(a => new ScheduleAllocation
        {
            StepId = a.StepId, ProductionOrderId = a.OrderId, WorkCenterId = a.WorkCenterId, Date = a.Date, Hours = a.Hours,
        }));

        var changedOrders = result.Orders.Where(o => !previous.TryGetValue(o.OrderId, out var before)
            || before.PlannedStart != o.Start || before.PlannedEnd != o.End || before.NoForecastReason != o.NoForecastReason).Select(o => o.OrderId).ToHashSet();
        var changedProjects = await WriteProjectForecastsAsync(result, run.Id, now, ct);
        run.OrdersCount = result.Orders.Count;
        run.StepsCount = result.Steps.Count;
        run.WithoutForecastCount = result.Orders.Count(o => o.NoForecastReason is not null);
        run.ChangedOrdersCount = changedOrders.Count;
        run.Error = null;

        // O risco do cartão segue a previsão do projeto: a OS de um projeto cuja previsão mudou também é avisada.
        foreach (var order in result.Orders.Where(o => changedProjects.Contains(o.ProjectId)))
        {
            changedOrders.Add(order.OrderId);
        }

        return new Written(changedOrders);
    }

    /// <summary>
    /// Previsão de cada projeto recalculado (CA 5 a 7): só os que mudaram (data, fim da fabricação, motivo ou OS do motivo) são gravados em
    /// <c>schedule_project_forecasts</c> e publicados no outbox, na mesma transação do plano, com o <c>ComputedAt</c> da execução.
    /// </summary>
    private async Task<HashSet<Guid>> WriteProjectForecastsAsync(ScheduleResult result, Guid runId, DateTimeOffset now, CancellationToken ct)
    {
        var forecasts = await projects.BuildAsync(result, ct);
        var ids = forecasts.Select(f => f.ProjectId).ToList();
        var stored = await plan.ScheduleProjectForecasts.Where(f => ids.Contains(f.ProjectId)).ToDictionaryAsync(f => f.ProjectId, ct);
        var changed = new HashSet<Guid>();
        foreach (var f in forecasts)
        {
            if (stored.TryGetValue(f.ProjectId, out var row)
                && row.ForecastDate == f.ForecastDate && row.FabricationEnd == f.FabricationEnd && row.Reason == f.Reason && row.ReasonOrderId == f.ReasonOrderId)
            {
                continue;
            }

            if (row is null)
            {
                row = new ScheduleProjectForecast { ProjectId = f.ProjectId };
                plan.ScheduleProjectForecasts.Add(row);
            }

            row.ForecastDate = f.ForecastDate;
            row.FabricationEnd = f.FabricationEnd;
            row.Reason = f.Reason;
            row.ReasonOrderId = f.ReasonOrderId;
            row.ComputedAt = now;
            row.RunId = runId;
            events.Publish(new ProjectForecastChanged(f.ProjectId, f.ForecastDate, f.FabricationEnd, f.Reason, now, f.ReasonOrderId));
            changed.Add(f.ProjectId);
        }

        return changed;
    }

    /// <summary>Depois do commit (CA 4): <c>OrderBoardChanged</c> só das OS que mudaram e o sinal <c>ScheduleChanged</c>; falha só vai para o log.</summary>
    private async Task NotifyAsync(IReadOnlyCollection<Guid> changedOrders, CancellationToken ct)
    {
        if (tenant.Tenant?.Id is not { } tenantId)
        {
            return;
        }

        try
        {
            if (changedOrders.Count > 0)
            {
                await push.OrdersChangedAsync(tenantId, changedOrders, ct);
            }

            await push.ScheduleChangedAsync(tenantId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPushFailed(logger, ex);
        }
    }

    /// <summary>Marca "pendente" (no máximo uma por tenant): com a marca já lá, o pedido se junta a ela. True quando a marca nasceu agora.</summary>
    private async Task<bool> MarkPendingAsync(ScheduleTrigger trigger, Guid? requestedBy, CancellationToken ct)
    {
        plan.ChangeTracker.Clear();
        if (await plan.ScheduleRuns.AnyAsync(r => r.Status == ScheduleRunStatus.Pending, ct))
        {
            return false;
        }

        plan.ScheduleRuns.Add(new ScheduleRun { Trigger = trigger, Status = ScheduleRunStatus.Pending, RequestedAt = clock.GetUtcNow(), RequestedBy = requestedBy });
        try
        {
            await plan.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, PendingIndex))
        {
            // Outro pedido marcou ao mesmo tempo: a marca é uma só.
            return false;
        }
        finally
        {
            plan.ChangeTracker.Clear();
        }
    }

    internal static ScheduleRunInfo Info(ScheduleRun r) =>
        new(r.Id, r.Trigger, r.Status, r.RequestedAt, r.StartedAt, r.FinishedAt, r.OrdersCount, r.StepsCount, r.ChangedOrdersCount, r.WithoutForecastCount, r.DurationMs, r.Error);

    private static string Truncate(string message) => message.Length <= MaxError ? message : message[..MaxError];

    [LoggerMessage(Level = LogLevel.Information, Message = "Plano recalculado ({Trigger}): {Orders} OS em {DurationMs} ms.")]
    private static partial void LogRun(ILogger logger, ScheduleTrigger trigger, int orders, int durationMs);

    [LoggerMessage(Level = LogLevel.Error, Message = "O recálculo do plano ({Trigger}) falhou; o plano anterior foi mantido.")]
    private static partial void LogFailed(ILogger logger, Exception exception, ScheduleTrigger trigger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "O aviso de plano novo ao Kanban e à TV falhou; as telas recarregam no próximo aviso.")]
    private static partial void LogPushFailed(ILogger logger, Exception exception);
}

/// <summary>Leituras do plano gravado compartilhadas pelo escritor e pelo contrato: a previsão da OS sai das etapas dela.</summary>
public static class ScheduleViews
{
    public sealed record Row(Guid StepId, Guid OrderId, int Sequence, decimal Hours, DateOnly? PlannedStart, DateOnly? PlannedEnd, string? NoForecastReason);

    /// <summary>Por OS: com algum motivo, sem data e o motivo da primeira etapa sem data; senão o menor início e o maior fim.</summary>
    public static Dictionary<Guid, OrderForecastInfo> ByOrder(IEnumerable<Row> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.GroupBy(r => r.OrderId).ToDictionary(g => g.Key, g =>
        {
            var steps = g.OrderBy(r => r.Sequence).Select(r => new StepForecastInfo(r.StepId, r.Sequence, r.Hours, r.PlannedStart, r.PlannedEnd, r.NoForecastReason)).ToList();
            return steps.FirstOrDefault(s => s.NoForecastReason is not null) is { } blocked
                ? new OrderForecastInfo(g.Key, null, null, blocked.NoForecastReason, steps)
                : new OrderForecastInfo(g.Key, steps.Min(s => s.PlannedStart), steps.Max(s => s.PlannedEnd), null, steps);
        });
    }
}
