using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary>Registro da auditoria lido por um módulo dono (ex.: as exportações anteriores, RH-03 CA 5).</summary>
public sealed record AuditRecord(Guid Id, string EntityId, string Action, Guid? UserId, DateTimeOffset At, string Changes);

/// <summary>Alteração de cadastro (criação, edição ou exclusão) lida da auditoria, com a entidade e a origem (FLH-01 CA 12).</summary>
public sealed record AuditChange(Guid Id, string Entity, string EntityId, string Action, Guid? UserId, DateTimeOffset At, string Changes, string? Origin);

/// <summary>
/// Leitura da auditoria pelo módulo dono de uma entidade lógica que não é tabela (ex.: <c>AccountantExport</c>): só a entidade e a ação
/// pedidas, do mais recente ao mais antigo. A permissão é conferida pelo módulo que chama.
/// </summary>
public interface IAuditReader
{
    Task<IReadOnlyList<AuditRecord>> ListAsync(string entity, string action, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Criações, edições e exclusões das entidades pedidas depois de <paramref name="since"/> (nulo = desde sempre), da mais antiga à mais
    /// recente; com <paramref name="usersOnly"/>, só as feitas por usuário (sem as do seed). A permissão é conferida pelo módulo que chama.
    /// </summary>
    Task<IReadOnlyList<AuditChange>> ChangesSinceAsync(IReadOnlyCollection<string> entities, DateTimeOffset? since, bool usersOnly, int take, CancellationToken cancellationToken = default);
}

public sealed class AuditReader(BemAquiDbContext db) : IAuditReader
{
    private static readonly string[] ChangeActions = [AuditActions.Created, AuditActions.Updated, AuditActions.Deleted];

    public async Task<IReadOnlyList<AuditRecord>> ListAsync(string entity, string action, int take, CancellationToken cancellationToken = default) =>
        await db.AuditEntries.AsNoTracking().Where(a => a.Entity == entity && a.Action == action)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(Math.Clamp(take, 1, 500))
            .Select(a => new AuditRecord(a.Id, a.EntityId, a.Action, a.UserId, a.At, a.Changes)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AuditChange>> ChangesSinceAsync(IReadOnlyCollection<string> entities, DateTimeOffset? since, bool usersOnly, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var query = db.AuditEntries.AsNoTracking().Where(a => entities.Contains(a.Entity) && ChangeActions.Contains(a.Action));
        if (since is { } from)
        {
            query = query.Where(a => a.At > from);
        }

        if (usersOnly)
        {
            query = query.Where(a => a.UserId != null);
        }

        var recent = await query.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).Take(Math.Clamp(take, 1, 500))
            .Select(a => new AuditChange(a.Id, a.Entity, a.EntityId, a.Action, a.UserId, a.At, a.Changes, a.Origin)).ToListAsync(cancellationToken);
        recent.Reverse();
        return recent;
    }
}
