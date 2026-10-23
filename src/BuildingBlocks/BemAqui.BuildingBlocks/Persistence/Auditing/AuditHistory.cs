using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary><c>Origin</c>: quem alterou quando não foi um usuário (ex.: a importação do Dolibarr, MIG-01a CA 21).</summary>
public sealed record AuditEntryDto(Guid Id, string Action, Guid? UserId, DateTimeOffset At, string Changes, string? Origin);

/// <summary>Entrada do histórico de um registro com os filhos: <c>Entity</c> diz de qual (ex.: <c>RoutingTemplateStep</c>).</summary>
public sealed record RelatedAuditEntryDto(Guid Id, string Entity, string EntityId, string Action, Guid? UserId, DateTimeOffset At, string Changes, string? Origin);

/// <summary>
/// Histórico de um registro, do mais recente ao mais antigo, só com a permissão de leitura do módulo dono (<c>view</c>, ou
/// <c>view_sensitive</c> no HR). As leituras de dado sensível (<c>SensitiveRead</c>) só aparecem para o Proprietário (ADR 0016).
/// </summary>
public sealed class AuditHistory(BemAquiDbContext db, AuditableEntityRegistry registry, IPermissionChecker permissions, IPermissionService effective, ICurrentUser currentUser)
{
    public async Task<Result<PagedList<AuditEntryDto>>> GetAsync(string entity, string entityId, PageRequest page, CancellationToken cancellationToken = default)
    {
        var module = registry.OwnerOf(entity);
        if (module is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Registro sem histórico de alterações.");
        }

        if (!await permissions.HasPermissionAsync(module, registry.ReadActionOf(entity), cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        var owner = currentUser.UserId is { } me && (await effective.GetAsync(me, cancellationToken)).IsOwner;
        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.Entity == entity && a.EntityId == entityId && (owner || a.Action != AuditActions.SensitiveRead))
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Select(a => new AuditEntryDto(a.Id, a.Action, a.UserId, a.At, a.Changes, a.Origin))
            .ToPagedListAsync(page, cancellationToken);
    }

    /// <summary>
    /// Histórico dos registros que pertencem a um documento de outro módulo, com os filhos de cada um (ex.: as revisões da BOM de uma OS e
    /// as linhas, ENG-02 CA 7 e 21): os registros são achados pela chave do pai gravada na criação ou na exclusão
    /// (<paramref name="parentProperty"/>, em camelCase), inclusive os já apagados (rascunho descartado), e os filhos pela chave de cada
    /// registro (<paramref name="childParentProperty"/>). Filhos do mesmo módulo do registro.
    /// </summary>
    public async Task<Result<PagedList<RelatedAuditEntryDto>>> GetByParentAsync(
        string entity, string parentProperty, string parentId, IReadOnlyList<string> childEntities, string childParentProperty, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(childEntities);
        var module = registry.OwnerOf(entity);
        if (module is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Registro sem histórico de alterações.");
        }

        if (childEntities.Any(c => registry.OwnerOf(c) != module))
        {
            throw new InvalidOperationException($"Os filhos de {entity} precisam ser entidades auditáveis do módulo {module}.");
        }

        if (!await permissions.HasPermissionAsync(module, registry.ReadActionOf(entity), cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        var owner = currentUser.UserId is { } me && (await effective.GetAsync(me, cancellationToken)).IsOwner;
        var (created, removed) = (KeyJson(parentProperty, "new", parentId), KeyJson(parentProperty, "old", parentId));
        var roots = await db.AuditEntries.AsNoTracking()
            .Where(a => a.Entity == entity && (EF.Functions.JsonContains(a.Changes, created) || EF.Functions.JsonContains(a.Changes, removed)))
            .Select(a => a.EntityId).Distinct().ToListAsync(cancellationToken);
        var children = new List<string>();
        foreach (var root in roots)
        {
            var (childCreated, childRemoved) = (KeyJson(childParentProperty, "new", root), KeyJson(childParentProperty, "old", root));
            children.AddRange(await db.AuditEntries.AsNoTracking()
                .Where(a => childEntities.Contains(a.Entity) && (EF.Functions.JsonContains(a.Changes, childCreated) || EF.Functions.JsonContains(a.Changes, childRemoved)))
                .Select(a => a.EntityId).Distinct().ToListAsync(cancellationToken));
        }

        return await db.AuditEntries.AsNoTracking()
            .Where(a => ((a.Entity == entity && roots.Contains(a.EntityId)) || (childEntities.Contains(a.Entity) && children.Contains(a.EntityId))) && (owner || a.Action != AuditActions.SensitiveRead))
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Select(a => new RelatedAuditEntryDto(a.Id, a.Entity, a.EntityId, a.Action, a.UserId, a.At, a.Changes, a.Origin))
            .ToPagedListAsync(page, cancellationToken);
    }

    private static string KeyJson(string property, string side, string value) =>
        JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>> { [property] = new() { [side] = value } });

    /// <summary>
    /// Histórico do registro e dos filhos (ex.: etapas e vínculos de produto do roteiro, ENG-01 CA 18): os filhos são achados
    /// pela chave do pai gravada na criação ou na exclusão (<paramref name="parentProperty"/>, em camelCase), inclusive os já
    /// removidos. Os filhos precisam ser do mesmo módulo do pai.
    /// </summary>
    public async Task<Result<PagedList<RelatedAuditEntryDto>>> GetWithChildrenAsync(string entity, string entityId, IReadOnlyList<string> childEntities, string parentProperty, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(childEntities);
        var module = registry.OwnerOf(entity);
        if (module is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Registro sem histórico de alterações.");
        }

        if (childEntities.Any(c => registry.OwnerOf(c) != module))
        {
            throw new InvalidOperationException($"Os filhos de {entity} precisam ser entidades auditáveis do módulo {module}.");
        }

        if (!await permissions.HasPermissionAsync(module, registry.ReadActionOf(entity), cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        }

        var owner = currentUser.UserId is { } me && (await effective.GetAsync(me, cancellationToken)).IsOwner;
        var created = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>> { [parentProperty] = new() { ["new"] = entityId } });
        var removed = JsonSerializer.Serialize(new Dictionary<string, Dictionary<string, string>> { [parentProperty] = new() { ["old"] = entityId } });
        var children = db.AuditEntries.Where(a => childEntities.Contains(a.Entity) && (EF.Functions.JsonContains(a.Changes, created) || EF.Functions.JsonContains(a.Changes, removed)))
            .Select(a => a.EntityId);
        return await db.AuditEntries.AsNoTracking()
            .Where(a => ((a.Entity == entity && a.EntityId == entityId) || (childEntities.Contains(a.Entity) && children.Contains(a.EntityId))) && (owner || a.Action != AuditActions.SensitiveRead))
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Select(a => new RelatedAuditEntryDto(a.Id, a.Entity, a.EntityId, a.Action, a.UserId, a.At, a.Changes, a.Origin))
            .ToPagedListAsync(page, cancellationToken);
    }
}
