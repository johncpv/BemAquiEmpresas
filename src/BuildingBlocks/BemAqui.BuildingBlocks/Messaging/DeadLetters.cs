using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Messaging;

public sealed record DeadLetteredMessage(Guid Id, string Type, string Payload, string Error);

/// <summary>
/// Reação a uma mensagem que foi para dead-letter (COM-04b CA 5, ADR 0019 §5): o módulo dono avisa quem pode reprocessar.
/// Roda no escopo do tenant, numa gravação própria; falhar aqui não desfaz o dead-letter.
/// </summary>
public interface IDeadLetterObserver
{
    Task OnDeadLetteredAsync(DeadLetteredMessage message, CancellationToken cancellationToken);
}

public sealed record DeadLetterView(Guid Id, string Type, DateTimeOffset OccurredAt, DateTimeOffset DeadLetteredAt, int Attempts, string? Error);

/// <summary>Lista e devolve para a fila as mensagens em dead-letter dos tipos que o chamador administra; a devolução é auditada.</summary>
public sealed class DeadLetterRequeue(BemAquiDbContext db, ICurrentUser currentUser, OutboxSignal signal, TimeProvider clock)
{
    public async Task<IReadOnlyList<DeadLetterView>> ListAsync(IReadOnlyCollection<string> types, CancellationToken cancellationToken = default) =>
        await db.OutboxMessages.AsNoTracking().Where(m => m.DeadLetteredAt != null && m.ProcessedAt == null && types.Contains(m.Type)).OrderBy(m => m.OccurredAt)
            .Select(m => new DeadLetterView(m.Id, m.Type, m.OccurredAt, m.DeadLetteredAt!.Value, m.Attempts, m.Error)).Take(100).ToListAsync(cancellationToken);

    public async Task<string?> PayloadAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.OutboxMessages.AsNoTracking().Where(m => m.Id == id).Select(m => m.Payload).FirstOrDefaultAsync(cancellationToken);

    public async Task<Result> RequeueAsync(Guid id, IReadOnlyCollection<string> types, CancellationToken cancellationToken = default)
    {
        var message = await db.OutboxMessages.FirstOrDefaultAsync(m => m.Id == id && types.Contains(m.Type), cancellationToken);
        if (message is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Mensagem não encontrada.");
        }

        if (message.DeadLetteredAt is null || message.ProcessedAt is not null)
        {
            return Error.Conflict("message_not_dead_lettered", "A mensagem já foi reenviada ou processada.");
        }

        var now = clock.GetUtcNow();
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = "OutboxMessage", EntityId = message.Id.ToString(), Action = AuditActions.Updated, UserId = currentUser.UserId, At = now,
            Changes = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["reprocessed"] = new { old = (object?)null, @new = message.Type },
                ["attempts"] = new { old = message.Attempts, @new = 0 },
            }),
        });
        message.DeadLetteredAt = null;
        message.Attempts = 0;
        message.Error = null;
        message.NextAttemptAt = now;
        await db.SaveChangesAsync(cancellationToken);
        signal.Notify();
        return Result.Success();
    }
}
