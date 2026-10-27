using BemAqui.BuildingBlocks.Identity;

namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>Registro na auditoria de algo que não é alteração de entidade (ADR 0016).</summary>
public interface IAuditWriter
{
    /// <summary>
    /// Leitura de dado sensível (<c>SensitiveRead</c>, CAD-04a CA 10): quem leu, quando e de qual IP, sem os valores lidos. Grava na
    /// hora, fora da transação do comando, porque a leitura não tem comando.
    /// </summary>
    Task RecordReadAsync(string entity, string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Leitura sensível com a referência de quem pediu (RH-02c CA 10: a conta de salário lida no "Pagar" leva o número da OP), sem o dado lido.
    /// </summary>
    Task RecordReadAsync(string entity, string entityId, string reference, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download de anexo de dono com dado pessoal (<c>AttachmentOwnerRules.AuditReads</c>, CAD-04c2 CA 12): <c>SensitiveRead</c> do dono
    /// (tipo e id), com o id do anexo baixado, quem, quando e o IP; sem o nome nem o conteúdo do arquivo.
    /// </summary>
    Task RecordAttachmentReadAsync(string ownerType, Guid ownerId, Guid attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exportação (<c>Exported</c>, EST-04 CA 4): quem exportou, quando, de qual registro, o formato e quantas linhas, sem o conteúdo. Grava na
    /// hora, como a leitura sensível.
    /// </summary>
    Task RecordExportAsync(string entity, string entityId, string format, int rows, CancellationToken cancellationToken = default);

    /// <summary>
    /// Importação de arquivo (<c>Imported</c>, RH-02a CA 9): quem importou, quando, em qual registro, o formato, quantas linhas e quantos
    /// registros mudaram, sem o arquivo nem os valores. Grava junto com o que estiver pendente no contexto (uma transação).
    /// </summary>
    Task RecordImportAsync(string entity, string entityId, string format, int rows, int records, CancellationToken cancellationToken = default);
}

public sealed class AuditWriter(BemAquiDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditWriter
{
    public async Task RecordReadAsync(string entity, string entityId, CancellationToken cancellationToken = default)
    {
        db.AuditEntries.Add(new AuditEntry { Entity = entity, EntityId = entityId, Action = AuditActions.SensitiveRead, UserId = currentUser.UserId, At = clock.GetUtcNow(), Ip = currentUser.IpAddress, Changes = "{}" });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordReadAsync(string entity, string entityId, string reference, CancellationToken cancellationToken = default)
    {
        var changes = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["reference"] = new { old = (string?)null, @new = reference } });
        db.AuditEntries.Add(new AuditEntry { Entity = entity, EntityId = entityId, Action = AuditActions.SensitiveRead, UserId = currentUser.UserId, At = clock.GetUtcNow(), Ip = currentUser.IpAddress, Changes = changes });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAttachmentReadAsync(string ownerType, Guid ownerId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var changes = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["attachmentId"] = new { old = (string?)null, @new = attachmentId } });
        db.AuditEntries.Add(new AuditEntry { Entity = ownerType, EntityId = ownerId.ToString(), Action = AuditActions.SensitiveRead, UserId = currentUser.UserId, At = clock.GetUtcNow(), Ip = currentUser.IpAddress, Changes = changes });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordExportAsync(string entity, string entityId, string format, int rows, CancellationToken cancellationToken = default)
    {
        var changes = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["format"] = new { old = (string?)null, @new = format }, ["rows"] = new { old = (int?)null, @new = rows } });
        db.AuditEntries.Add(new AuditEntry { Entity = entity, EntityId = entityId, Action = AuditActions.Exported, UserId = currentUser.UserId, At = clock.GetUtcNow(), Changes = changes });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordImportAsync(string entity, string entityId, string format, int rows, int records, CancellationToken cancellationToken = default)
    {
        var changes = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["format"] = new { old = (string?)null, @new = format },
            ["rows"] = new { old = (int?)null, @new = rows },
            ["records"] = new { old = (int?)null, @new = records },
        });
        db.AuditEntries.Add(new AuditEntry { Entity = entity, EntityId = entityId, Action = AuditActions.Imported, UserId = currentUser.UserId, At = clock.GetUtcNow(), Ip = currentUser.IpAddress, Changes = changes });
        await db.SaveChangesAsync(cancellationToken);
    }
}
