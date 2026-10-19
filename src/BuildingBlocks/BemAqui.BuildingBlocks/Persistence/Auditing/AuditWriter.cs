using BemAqui.BuildingBlocks.Identity;

namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>Registro na auditoria de algo que não é alteração de entidade (ADR 0016).</summary>
public interface IAuditWriter
{
    /// <summary>
    /// Leitura de dado sensível (<c>SensitiveRead</c>, CAD-04a CA 10): quem leu e quando, sem os valores lidos. Grava na hora,
    /// fora da transação do comando, porque a leitura não tem comando.
    /// </summary>
    Task RecordReadAsync(string entity, string entityId, CancellationToken cancellationToken = default);
}

public sealed class AuditWriter(BemAquiDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditWriter
{
    public async Task RecordReadAsync(string entity, string entityId, CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(new AuditEntry { Entity = entity, EntityId = entityId, Action = AuditActions.SensitiveRead, UserId = currentUser.UserId, At = clock.GetUtcNow(), Changes = "{}" });
        await db.SaveChangesAsync(cancellationToken);
    }
}
