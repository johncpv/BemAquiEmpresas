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

    /// <summary>
    /// Exportação (<c>Exported</c>, EST-04 CA 4): quem exportou, quando, de qual registro, o formato e quantas linhas, sem o conteúdo. Grava na
    /// hora, como a leitura sensível.
    /// </summary>
    Task RecordExportAsync(string entity, string entityId, string format, int rows, CancellationToken cancellationToken = default);
}

public sealed class AuditWriter(BemAquiDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditWriter
{
    public async Task RecordReadAsync(string entity, string entityId, CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(new AuditEntry { Entity = entity, EntityId = entityId, Action = AuditActions.SensitiveRead, UserId = currentUser.UserId, At = clock.GetUtcNow(), Changes = "{}" });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordExportAsync(string entity, string entityId, string format, int rows, CancellationToken cancellationToken = default)
    {
        var changes = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["format"] = new { old = (string?)null, @new = format }, ["rows"] = new { old = (int?)null, @new = rows } });
        db.AuditEntries.Add(new AuditEntry { Entity = entity, EntityId = entityId, Action = AuditActions.Exported, UserId = currentUser.UserId, At = clock.GetUtcNow(), Changes = changes });
        await db.SaveChangesAsync(cancellationToken);
    }
}
