using System.Text.Json;
using BemAqui.Commercial.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>Fila efetiva: a ordem (as gravadas mais as que entrariam agora pela data prometida), os candidatos e a versão.</summary>
public sealed record PlanningQueueSnapshot(IReadOnlyList<Guid> Order, IReadOnlyDictionary<Guid, QueueCandidate> Candidates, QueueSync Sync, uint Version);

/// <summary>
/// Fila de prioridade dos projetos (PLN-02, ADR 0029 §3): lê a fila efetiva sem gravar (<see cref="SnapshotAsync"/>, a tela e o
/// <see cref="IPlanningQueue"/>) e a mantém (<see cref="SyncAsync"/>: entrada pela data prometida, saída quando as OS de fabricação
/// terminam ou são canceladas, posições contíguas, histórico com o motivo e quem = Sistema). A manutenção roda no recálculo do plano
/// (dentro da transação do <c>ScheduleService</c>) e no "Salvar ordem", sempre sob o advisory lock da fila.
/// </summary>
public sealed class PlanningQueueStore(IProductionDb db, IProjects projects, TimeProvider clock) : IPlanningQueue
{
    /// <summary>Chave do advisory lock da fila (o banco é por tenant, então a chave fixa já é por tenant).</summary>
    public const string LockKey = "production.planning_queue";

    public const string EnteredReason = "Entrou pela data prometida";

    public const string InitialReason = "Fila montada pela data prometida";

    public const string LeftCompletedReason = "Saiu: OS concluídas";

    public const string LeftCancelledReason = "Saiu: OS canceladas";

    internal static readonly ProductionOrderStatus[] OpenStatuses =
        [ProductionOrderStatus.Open, ProductionOrderStatus.InProgress, ProductionOrderStatus.Paused, ProductionOrderStatus.Quality, ProductionOrderStatus.ReadyToComplete];

    private static readonly JsonSerializerOptions Json = new();

    /// <summary>CA 9: os ids dos projetos na ordem da fila (sem gravar nada), para o motor, o painel da TV e a simulação do orçamento.</summary>
    public async Task<IReadOnlyList<Guid>> OrderAsync(CancellationToken cancellationToken = default) => (await SnapshotAsync(cancellationToken)).Order;

    /// <summary>A fila como ela fica com a manutenção de agora, sem gravar: a tela mostra já na posição da data prometida (propositivo).</summary>
    public async Task<PlanningQueueSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var current = await db.PlanningQueue.AsNoTracking().OrderBy(e => e.Position).ThenBy(e => e.Id).Select(e => e.ProjectId).ToListAsync(cancellationToken);
        var candidates = await CandidatesAsync(cancellationToken);
        var version = await db.PlanningQueueStates.AsNoTracking().Select(s => (uint?)s.Version).FirstOrDefaultAsync(cancellationToken) ?? 0u;
        var sync = PlanningQueueRules.Sync(current, [.. candidates.Values]);
        return new PlanningQueueSnapshot(sync.Order, candidates, sync, version);
    }

    /// <summary>
    /// Mantém a fila gravada (CA 2 e 3), sob o advisory lock da fila: na transação de quem chama (o recálculo do plano ou o "Salvar ordem")
    /// ou numa própria. Grava as entradas e saídas com o histórico (Sistema) e devolve a ordem gravada.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> SyncAsync(CancellationToken cancellationToken = default)
    {
        var own = db.Database.CurrentTransaction is null;
        await using var transaction = own ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        await LockAsync(cancellationToken);

        var entries = await db.PlanningQueue.OrderBy(e => e.Position).ThenBy(e => e.Id).ToListAsync(cancellationToken);
        var current = entries.Select(e => e.ProjectId).ToList();
        var candidates = await CandidatesAsync(cancellationToken);
        var sync = PlanningQueueRules.Sync(current, [.. candidates.Values]);
        if (!sync.Changed && entries.Select((e, i) => e.Position == i + 1).All(ok => ok))
        {
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return sync.Order;
        }

        var now = clock.GetUtcNow();
        var leftReasons = await LeftReasonsAsync(sync.Left, cancellationToken);
        var byProject = entries.ToDictionary(e => e.ProjectId);
        foreach (var id in sync.Left)
        {
            db.PlanningQueue.Remove(byProject[id]);
        }

        var positions = PlanningQueueRules.Positions(sync.Order);
        foreach (var id in sync.Order)
        {
            if (byProject.TryGetValue(id, out var entry))
            {
                entry.Position = positions[id];
            }
            else
            {
                db.PlanningQueue.Add(new PlanningQueueEntry { ProjectId = id, Position = positions[id], CreatedAt = now });
            }
        }

        var oldPositions = PlanningQueueRules.Positions(current);
        if (sync.Initial)
        {
            db.PlanningQueueChanges.Add(Change(now, PlanningQueueChangeKind.Initial, null, InitialReason, PlanningQueueRules.Moves([], sync.Order)));
        }
        else
        {
            foreach (var id in sync.Left)
            {
                db.PlanningQueueChanges.Add(Change(now, PlanningQueueChangeKind.Left, id, leftReasons.GetValueOrDefault(id, LeftCompletedReason), [new QueueMove(id, oldPositions[id], null)]));
            }

            foreach (var id in sync.Entered)
            {
                db.PlanningQueueChanges.Add(Change(now, PlanningQueueChangeKind.Entered, id, EnteredReason, [new QueueMove(id, null, positions[id])]));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return sync.Order;
    }

    /// <summary>Advisory lock da fila até o fim da transação (espera quem está gravando: a manutenção e o salvar são curtos).</summary>
    public async Task LockAsync(CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({LockKey}, 0))", cancellationToken);

    /// <summary>Linha do histórico: <paramref name="userId"/> nulo = Sistema.</summary>
    public static PlanningQueueChange Change(DateTimeOffset at, PlanningQueueChangeKind kind, Guid? projectId, string reason, IReadOnlyList<QueueMove> moves, Guid? userId = null) =>
        new() { At = at, Kind = kind, ProjectId = projectId, Reason = reason, Moves = JsonSerializer.Serialize(moves, Json), UserId = userId };

    public static IReadOnlyList<QueueMove> ReadMoves(string json) =>
        string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<QueueMove>>(json, Json) ?? [];

    /// <summary>
    /// Projetos com OS de fabricação não concluída e não cancelada (CA 1), com a data prometida, a assinatura e o número pelo
    /// <see cref="IProjects"/>; projeto que o Commercial não acha entra sem datas (vai para o fim).
    /// </summary>
    private async Task<Dictionary<Guid, QueueCandidate>> CandidatesAsync(CancellationToken ct)
    {
        var ids = await db.ProductionOrders.AsNoTracking()
            .Where(o => o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status))
            .Select(o => o.ProjectId).Distinct().ToListAsync(ct);
        if (ids.Count == 0)
        {
            return [];
        }

        var summaries = (await projects.FindManyAsync(ids, ct)).ToDictionary(p => p.Id);
        return ids.ToDictionary(id => id, id => summaries.TryGetValue(id, out var p)
            ? new QueueCandidate(id, p.Number, p.PromisedDate, p.SignedAt)
            : new QueueCandidate(id, string.Empty, null, null));
    }

    /// <summary>Motivo da saída: alguma OS de fabricação concluída → "OS concluídas"; senão todas canceladas.</summary>
    private async Task<Dictionary<Guid, string>> LeftReasonsAsync(IReadOnlyList<Guid> left, CancellationToken ct)
    {
        if (left.Count == 0)
        {
            return [];
        }

        var ids = left.ToList();
        var completed = await db.ProductionOrders.AsNoTracking()
            .Where(o => ids.Contains(o.ProjectId) && o.Type == ProductionOrderType.Fabrication && o.Status == ProductionOrderStatus.Completed)
            .Select(o => o.ProjectId).Distinct().ToListAsync(ct);
        return ids.ToDictionary(id => id, id => completed.Contains(id) ? LeftCompletedReason : LeftCancelledReason);
    }
}
