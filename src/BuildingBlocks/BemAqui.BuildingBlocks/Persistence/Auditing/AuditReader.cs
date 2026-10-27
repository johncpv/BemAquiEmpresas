using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>Registro da auditoria lido por um módulo dono (ex.: as exportações anteriores, RH-03 CA 5).</summary>
public sealed record AuditRecord(Guid Id, string EntityId, string Action, Guid? UserId, DateTimeOffset At, string Changes);

/// <summary>
/// Leitura da auditoria pelo módulo dono de uma entidade lógica que não é tabela (ex.: <c>AccountantExport</c>): só a entidade e a ação
/// pedidas, do mais recente ao mais antigo. A permissão é conferida pelo módulo que chama.
/// </summary>
public interface IAuditReader
{
    Task<IReadOnlyList<AuditRecord>> ListAsync(string entity, string action, int take, CancellationToken cancellationToken = default);
}

public sealed class AuditReader(BemAquiDbContext db) : IAuditReader
{
    public async Task<IReadOnlyList<AuditRecord>> ListAsync(string entity, string action, int take, CancellationToken cancellationToken = default) =>
        await db.AuditEntries.AsNoTracking().Where(a => a.Entity == entity && a.Action == action)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(Math.Clamp(take, 1, 500))
            .Select(a => new AuditRecord(a.Id, a.EntityId, a.Action, a.UserId, a.At, a.Changes)).ToListAsync(cancellationToken);
}
