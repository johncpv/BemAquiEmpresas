using System.Diagnostics;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Aba Fila do Planejamento (PLN-02): a fila com prometido, previsto, folga e risco (CA 1); a prévia "antes → depois" pelo motor em
/// memória, sem gravar (CA 4 e 5); o "Salvar ordem" com a versão, a permutação exata, o histórico e o pedido de recálculo (CA 6 e 7); e as
/// mudanças manuais e automáticas (CA 8).
/// </summary>
public sealed class PlanningQueueService(
    IProductionDb db, PlanningQueueStore store, IProjects projects, IParties parties, IUserDirectory users, ICurrentUser currentUser, ScheduleInputBuilder builder,
    ProjectForecastBuilder forecasts, IScheduleTrigger trigger, TimeProvider clock)
{
    /// <summary>Política nomeada do rate limit da prévia (CA 10): 20 por minuto por usuário.</summary>
    public const string SimulatePolicy = "planning-simulate";

    public const int SimulatePerMinute = 20;

    public const string SimulateLimitMessage = "Aguarde alguns segundos para simular de novo.";

    public const string ManualReason = "Ordem alterada";

    public static Error Stale() => Error.Validation("planning_queue.changed", "A fila mudou. Recarregue e reordene de novo.");

    public static Error Conflict() => Error.Conflict("concurrency_conflict", "A fila mudou desde que você abriu. Recarregue.");

    public async Task<PlanningQueueView> GetAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await store.SnapshotAsync(cancellationToken);
        return await ViewAsync(snapshot, cancellationToken);
    }

    /// <summary>
    /// Prévia (CA 4 e 5): roda o motor duas vezes em memória, com a ordem gravada e com a nova (a mesma entrada, só a posição muda), e
    /// calcula a data prevista de cada projeto como o recálculo faria (fim da fabricação + prazo de instalação). Nada é gravado.
    /// </summary>
    public async Task<Result<PlanningQueuePreview>> PreviewAsync(PlanningQueuePreviewInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var started = Stopwatch.GetTimestamp();
        var snapshot = await store.SnapshotAsync(cancellationToken);
        if (!PlanningQueueRules.IsPermutation(snapshot.Order, input.Order))
        {
            return Stale();
        }

        var proposed = input.Order!;
        var schedule = await builder.BuildAsync(snapshot.Order, cancellationToken);
        var positions = PlanningQueueRules.Positions(proposed);
        var reordered = schedule with
        {
            Projects = [.. schedule.Projects.Select(p => p with { QueuePosition = positions.TryGetValue(p.ProjectId, out var at) ? at : null })],
        };
        var results = await forecasts.BuildManyAsync([FiniteScheduler.Run(schedule), FiniteScheduler.Run(reordered)], cancellationToken);
        var before = results[0].ToDictionary(f => f.ProjectId);
        var after = results[1].ToDictionary(f => f.ProjectId);
        var promised = (await projects.FindManyAsync(proposed, cancellationToken)).ToDictionary(p => p.Id, p => p.PromisedDate);
        var today = BusinessTime.Today(clock);
        var oldPositions = PlanningQueueRules.Positions(snapshot.Order);

        var rows = proposed.Select(id =>
        {
            var date = promised.GetValueOrDefault(id);
            var was = before.GetValueOrDefault(id)?.ForecastDate;
            var will = after.GetValueOrDefault(id)?.ForecastDate;
            return new PlanningQueuePreviewRow(id, oldPositions[id], positions[id], date, was, will, DeliveryRiskRules.SlackDays(date, was), DeliveryRiskRules.SlackDays(date, will),
                DeliveryRiskRules.Evaluate(date, was, today, false).Risk, DeliveryRiskRules.Evaluate(date, will, today, false).Risk, Worsened(date, was, will),
                date is { } p && will is { } w && w > p ? w.DayNumber - p.DayNumber : null);
        }).ToList();
        return new PlanningQueuePreview(rows, (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>
    /// Passa a atrasar ou piora (CA 5): a previsão nova fica depois do prometido e é pior que a anterior (ou antes não havia previsão).
    /// </summary>
    public static bool Worsened(DateOnly? promised, DateOnly? before, DateOnly? after) =>
        promised is { } p && after is { } a && a > p && (before is not { } b || a > b);

    /// <summary>
    /// "Salvar ordem" (CA 6 e 7), numa transação sob o lock da fila: mantém a fila (quem entrou ou saiu desde a abertura), confere a versão
    /// (409) e a permutação exata (422), grava as posições, a versão, o histórico (quem, quando, antes → depois) e pede o recálculo do plano,
    /// que sai depois do commit.
    /// </summary>
    public async Task<Result<PlanningQueueSaved>> SaveAsync(PlanningQueueSaveInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var changed = false;
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            var order = await store.SyncAsync(cancellationToken);
            var state = await db.PlanningQueueStates.FirstOrDefaultAsync(cancellationToken);
            if ((state?.Version ?? 0u) != input.Version)
            {
                await transaction.CommitAsync(cancellationToken);
                return Conflict();
            }

            if (!PlanningQueueRules.IsPermutation(order, input.Order))
            {
                await transaction.CommitAsync(cancellationToken);
                return Stale();
            }

            var proposed = input.Order!;
            var moves = PlanningQueueRules.Moves(order, proposed);
            if (moves.Count > 0)
            {
                var now = clock.GetUtcNow();
                var positions = PlanningQueueRules.Positions(proposed);
                foreach (var entry in await db.PlanningQueue.ToListAsync(cancellationToken))
                {
                    entry.Position = positions[entry.ProjectId];
                }

                if (state is null)
                {
                    state = new PlanningQueueState();
                    db.PlanningQueueStates.Add(state);
                }

                state.ReorderedBy = currentUser.UserId;
                state.ReorderedAt = now;
                db.PlanningQueueChanges.Add(PlanningQueueStore.Change(now, PlanningQueueChangeKind.Manual, null, ManualReason, moves, currentUser.UserId));
                await trigger.RequestAsync("fila", cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                changed = true;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return new PlanningQueueSaved(changed, changed, await GetAsync(cancellationToken));
    }

    /// <summary>Mudanças na fila (CA 8), da mais recente à mais antiga, com quem (ou "Sistema") e o número de cada projeto.</summary>
    public async Task<PagedList<PlanningQueueChangeView>> ChangesAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var rows = await db.PlanningQueueChanges.AsNoTracking().OrderByDescending(c => c.At).ThenByDescending(c => c.Id).ToPagedListAsync(page, cancellationToken);
        var moves = rows.Items.ToDictionary(c => c.Id, c => PlanningQueueStore.ReadMoves(c.Moves));
        var projectIds = moves.Values.SelectMany(m => m).Select(m => m.ProjectId).Distinct().ToList();
        var numbers = (await projects.FindManyAsync(projectIds, cancellationToken)).ToDictionary(p => p.Id, p => p.Number);
        var names = new Dictionary<Guid, string>();
        foreach (var userId in rows.Items.Where(c => c.UserId is not null).Select(c => c.UserId!.Value).Distinct())
        {
            names[userId] = await users.DisplayNameAsync(userId, cancellationToken) ?? "Usuário removido";
        }

        var items = rows.Items.Select(c => new PlanningQueueChangeView(c.Id, c.At, c.UserId is { } u ? names[u] : "Sistema", c.Kind, c.Reason,
            [.. moves[c.Id].Select(m => new PlanningQueueMoveView(m.ProjectId, numbers.GetValueOrDefault(m.ProjectId) ?? "Projeto removido", m.From, m.To))])).ToList();
        return new PagedList<PlanningQueueChangeView>(items, rows.Page, rows.PageSize, rows.Total);
    }

    /// <summary>
    /// Monta as linhas (CA 1): OS de fabricação abertas e horas restantes do plano por projeto, a previsão publicada (PLN-01b), prometido e
    /// cliente pelos contratos (uma consulta de cada, sem N+1).
    /// </summary>
    private async Task<PlanningQueueView> ViewAsync(PlanningQueueSnapshot snapshot, CancellationToken ct)
    {
        var ids = snapshot.Order.ToList();
        var state = await db.PlanningQueueStates.AsNoTracking().FirstOrDefaultAsync(ct);
        var reorderedBy = state?.ReorderedBy is { } by ? await users.DisplayNameAsync(by, ct) : null;
        if (ids.Count == 0)
        {
            return new PlanningQueueView(snapshot.Version, state?.ReorderedAt, reorderedBy, []);
        }

        var open = db.ProductionOrders.AsNoTracking()
            .Where(o => o.Type == ProductionOrderType.Fabrication && PlanningQueueStore.OpenStatuses.Contains(o.Status) && ids.Contains(o.ProjectId));
        var orders = await open.Select(o => new { o.Id, o.Number, o.ProjectId }).ToListAsync(ct);
        var hours = await db.ScheduleStepForecastsView.Join(open, f => f.ProductionOrderId, o => o.Id, (f, o) => new { o.ProjectId, f.Hours })
            .GroupBy(x => x.ProjectId).Select(g => new { ProjectId = g.Key, Hours = g.Sum(x => x.Hours) }).ToDictionaryAsync(x => x.ProjectId, x => x.Hours, ct);
        var planned = await db.ScheduleProjectForecastsView.Where(f => ids.Contains(f.ProjectId)).ToDictionaryAsync(f => f.ProjectId, ct);
        var summaries = (await projects.FindManyAsync(ids, ct)).ToDictionary(p => p.Id);
        var partyNames = (await parties.FindManyAsync([.. summaries.Values.Select(p => p.PartyId).Distinct()], ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var numbers = orders.ToDictionary(o => o.Id, o => o.Number);
        var count = orders.GroupBy(o => o.ProjectId).ToDictionary(g => g.Key, g => g.Count());
        var today = BusinessTime.Today(clock);

        var rows = ids.Select((id, i) =>
        {
            var summary = summaries.GetValueOrDefault(id);
            var forecast = planned.GetValueOrDefault(id);
            var promised = summary?.PromisedDate;
            var date = forecast?.ForecastDate;
            var reasonOrder = forecast?.ReasonOrderId;
            return new PlanningQueueRow(i + 1, id, summary?.Number ?? "Projeto removido", summary?.Name ?? string.Empty, summary?.PartyId,
                summary is null ? null : partyNames.GetValueOrDefault(summary.PartyId), count.GetValueOrDefault(id), Math.Round(hours.GetValueOrDefault(id), 4),
                promised, date, DeliveryRiskRules.SlackDays(promised, date), DeliveryRiskRules.Evaluate(promised, date, today, false).Risk, date is not null,
                date is null ? forecast?.Reason : null, date is null ? reasonOrder : null, date is null && reasonOrder is { } o ? numbers.GetValueOrDefault(o) : null);
        }).ToList();
        return new PlanningQueueView(snapshot.Version, state?.ReorderedAt, reorderedBy, rows);
    }
}
