using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.BuildingBlocks.Persistence.Auditing;

/// <summary><c>Origin</c>: quem alterou quando não foi um usuário (ex.: a importação do Dolibarr, MIG-01a CA 21).</summary>
public sealed record AuditEntryDto(Guid Id, string Action, Guid? UserId, DateTimeOffset At, string Changes, string? Origin);

/// <summary>Histórico de um registro, do mais recente ao mais antigo, só com a permissão de leitura do módulo dono (<c>view</c>, ou <c>view_sensitive</c> no HR).</summary>
public sealed class AuditHistory(BemAquiDbContext db, AuditableEntityRegistry registry, IPermissionChecker permissions)
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

        return await db.AuditEntries.AsNoTracking()
            .Where(a => a.Entity == entity && a.EntityId == entityId)
            .OrderByDescending(a => a.At).ThenByDescending(a => a.Id)
            .Select(a => new AuditEntryDto(a.Id, a.Action, a.UserId, a.At, a.Changes, a.Origin))
            .ToPagedListAsync(page, cancellationToken);
    }
}
