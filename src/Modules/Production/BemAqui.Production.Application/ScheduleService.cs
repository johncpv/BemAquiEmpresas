using System.Diagnostics;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Production.Application;

/// <summary>Resultado do pedido de recálculo: <c>Ran</c> com a execução (sucesso ou erro) ou <c>Queued</c> (marcado "pendente" ou na fila).</summary>
public sealed record ScheduleRecalculation(bool Ran, bool Queued, ScheduleRunInfo? Run);

/// <summary>
/// Único escritor do plano (PLN-01a CA 10 a 12, ADR 0029 §2): monta a entrada, roda o <see cref="FiniteScheduler"/> e, numa transação só,
/// substitui <c>schedule_step_forecasts</c> e <c>schedule_allocations</c> e grava a execução em <c>schedule_runs</c>. Nunca toca
/// <c>production_orders</c> nem <c>production_order_steps</c> (a versão e o histórico da OS não mudam); as tabelas do plano ficam fora da
/// auditoria. Advisory lock por tenant (<c>pg_try_advisory_xact_lock</c>): o pedido que chega durante o cálculo marca "pendente" (no
/// máximo uma marca, índice único parcial) e quem termina roda uma vez para ela; nunca dois em paralelo nem fila infinita. Execução com
/// erro mantém o plano anterior e registra o erro.
/// </summary>
public sealed partial class ScheduleService(IProductionPlanDb plan, ScheduleInputBuilder builder, TimeProvider clock, ILogger<ScheduleService> logger)
{
    /// <summary>Chave do advisory lock; cada tenant tem banco próprio, então a chave fixa já é por tenant.</summary>
    public const string LockKey = "production.schedule";

    public const string PendingIndex = "ux_schedule_runs_pending";

    private const int MaxError = 2000;

    private const int MaxReason = 200;

    private enum Attempt
    {
        Ran,
        Busy,
        NothingPending,
    }

    public async Task<ScheduleRecalculation> RecalculateAsync(ScheduleTrigger trigger, Guid? requestedBy, CancellationToken cancellationToken = default)
    {
        var (state, run) = await TryRunAsync(trigger, requestedBy, onlyIfPending: false, cancellationToken);
        if (state == Attempt.Busy)
        {
            await MarkPendingAsync(trigger, requestedBy, cancellationToken);

            // Quem está calculando pode ter terminado entre a tentativa e a marca: tenta de novo, só se a marca ainda está lá.
            var (retried, retryRun) = await TryRunAsync(trigger, requestedBy, onlyIfPending: true, cancellationToken);
            return retried == Attempt.Ran ? new ScheduleRecalculation(true, false, retryRun) : new ScheduleRecalculation(false, true, null);
        }

        // O pedido que chegou durante este cálculo roda uma vez agora (a marca é uma só, por mais pedidos que tenham chegado).
        await TryRunAsync(trigger, null, onlyIfPending: true, cancellationToken);
        return new ScheduleRecalculation(true, false, run);
    }

    private async Task<(Attempt State, ScheduleRunInfo? Run)> TryRunAsync(ScheduleTrigger trigger, Guid? requestedBy, bool onlyIfPending, CancellationToken ct)
    {
        plan.ChangeTracker.Clear();
        var started = clock.GetTimestamp();
        var now = clock.GetUtcNow();
        ScheduleRun run;
        await using (var transaction = await plan.Database.BeginTransactionAsync(ct))
        {
            var locked = await plan.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({LockKey}, 0)) AS \"Value\"").SingleAsync(ct);
            if (!locked)
            {
                await transaction.RollbackAsync(ct);
                return (Attempt.Busy, null);
            }

            var pending = await plan.ScheduleRuns.Where(r => r.Status == ScheduleRunStatus.Pending).OrderBy(r => r.RequestedAt).FirstOrDefaultAsync(ct);
            if (onlyIfPending && pending is null)
            {
                await transaction.CommitAsync(ct);
                return (Attempt.NothingPending, null);
            }

            run = pending ?? new ScheduleRun { Trigger = trigger, RequestedAt = now, RequestedBy = requestedBy };
            run.StartedAt = now;
            try
            {
                await WriteAsync(run, ct);
                run.Status = ScheduleRunStatus.Succeeded;
                run.FinishedAt = clock.GetUtcNow();
                run.DurationMs = (int)clock.GetElapsedTime(started).TotalMilliseconds;
                if (pending is null)
                {
                    plan.ScheduleRuns.Add(run);
                }

                await plan.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                LogRun(logger, run.Trigger, run.OrdersCount, run.DurationMs);
                return (Attempt.Ran, Info(run));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, run.Trigger);
                await transaction.RollbackAsync(CancellationToken.None);
                run.Error = Truncate(ex.Message);
            }
        }

        // Execução com erro: o plano anterior fica inteiro (a transação voltou) e o erro vai para schedule_runs (alerta de job, FND-12).
        plan.ChangeTracker.Clear();
        var failed = await plan.ScheduleRuns.FirstOrDefaultAsync(r => r.Id == run.Id, CancellationToken.None)
            ?? plan.ScheduleRuns.Add(new ScheduleRun { Trigger = run.Trigger, RequestedAt = run.RequestedAt, RequestedBy = run.RequestedBy }).Entity;
        failed.Status = ScheduleRunStatus.Failed;
        failed.StartedAt = run.StartedAt;
        failed.FinishedAt = clock.GetUtcNow();
        failed.DurationMs = (int)clock.GetElapsedTime(started).TotalMilliseconds;
        failed.Error = run.Error;
        await plan.SaveChangesAsync(CancellationToken.None);
        return (Attempt.Ran, Info(failed));
    }

    /// <summary>Calcula e substitui o plano (dentro da transação do lock): as linhas antigas saem e entram as novas, com a contagem de OS mudadas.</summary>
    private async Task WriteAsync(ScheduleRun run, CancellationToken ct)
    {
        var input = await builder.BuildAsync(cancellationToken: ct);
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

        run.OrdersCount = result.Orders.Count;
        run.StepsCount = result.Steps.Count;
        run.WithoutForecastCount = result.Orders.Count(o => o.NoForecastReason is not null);
        run.ChangedOrdersCount = result.Orders.Count(o => !previous.TryGetValue(o.OrderId, out var before)
            || before.PlannedStart != o.Start || before.PlannedEnd != o.End || before.NoForecastReason != o.NoForecastReason);
        run.Error = null;
    }

    /// <summary>Marca "pendente" (no máximo uma por tenant): com a marca já lá, o pedido se junta a ela.</summary>
    private async Task MarkPendingAsync(ScheduleTrigger trigger, Guid? requestedBy, CancellationToken ct)
    {
        plan.ChangeTracker.Clear();
        if (await plan.ScheduleRuns.AnyAsync(r => r.Status == ScheduleRunStatus.Pending, ct))
        {
            return;
        }

        plan.ScheduleRuns.Add(new ScheduleRun { Trigger = trigger, Status = ScheduleRunStatus.Pending, RequestedAt = clock.GetUtcNow(), RequestedBy = requestedBy });
        try
        {
            await plan.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, PendingIndex))
        {
            // Outro pedido marcou ao mesmo tempo: a marca é uma só.
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
