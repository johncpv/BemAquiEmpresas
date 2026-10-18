using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed record GroupSummary(Guid Id, string Name, string Description, bool IsSystem, bool IsActive, bool RequiresTwoFactor, int MemberCount);

public sealed record GroupActionInfo(string Code, string Label, string Description, bool IsStandard);

/// <summary>Linha da matriz como a tela mostra: liberado no tenant, habilitado no grupo, ações marcadas e disponíveis.</summary>
public sealed record GroupModuleRow(string Code, string Name, string Area, bool Released, bool Enabled, IReadOnlyList<string> Actions, IReadOnlyList<GroupActionInfo> Available, string? ApproveMeaning);

public sealed record GroupMember(Guid UserId, string Name, string Email);

public sealed record GroupDetail(GroupSummary Group, IReadOnlyList<GroupMember> Members, IReadOnlyList<GroupModuleRow> Modules);

public sealed record SaveGroupRequest(string Name, string? Description, bool RequiresTwoFactor, IReadOnlyList<MatrixEntry> Modules);

/// <summary>Grupo salvo: quantos usuários recebem as novas permissões no próximo acesso (toast do protótipo).</summary>
public sealed record GroupSaved(Guid Id, int AffectedUsers);

/// <summary>
/// Grupos e matriz de permissões (FND-06b). Regras da matriz e delegação (ADR 0008) valem aqui, no servidor; o
/// Proprietário é protegido e salvar invalida a permissão efetiva dos membros (vale no próximo request).
/// </summary>
public sealed partial class GroupService(IIdentityDb db, IPermissionService permissions, BemAqui.BuildingBlocks.Notifications.INotificationService notifications, TenantContextHolder tenant, TimeProvider clock)
{
    public const string SystemGroupMessage = "Grupo do sistema: acesso total, não pode ser restringido.";
    public const string OwnerNeedsMemberMessage = "O grupo Proprietário precisa ter ao menos um membro.";

    public async Task<IReadOnlyList<GroupSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var counts = await db.UserGroups.GroupBy(ug => ug.GroupId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return (await db.Groups.AsNoTracking().ToListAsync(cancellationToken))
            .OrderByDescending(g => g.IsSystem).ThenByDescending(g => g.IsActive).ThenBy(g => g.Name, StringComparer.CurrentCulture)
            .Select(g => Summary(g, counts.GetValueOrDefault(g.Id)))
            .ToList();
    }

    public async Task<Result<GroupDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        var members = await db.UserGroups.Where(ug => ug.GroupId == id)
            .Join(db.Users, ug => ug.UserId, u => u.Id, (_, u) => new GroupMember(u.Id, u.Name, u.Email))
            .ToListAsync(cancellationToken);
        var grants = await GrantsAsync(id, cancellationToken);
        var rows = ModuleCatalog.All
            .OrderBy(m => Array.IndexOf(ModuleAreas.All, m.Area)).ThenBy(m => m.Order)
            .Select(m => new GroupModuleRow(
                m.Code,
                m.Name,
                m.Area,
                tenant.Resolved?.IsReleased(m.Code) == true,
                group.IsOwner || grants.Enabled.Contains(m.Code),
                group.IsOwner ? m.Actions.ToList() : grants.Actions.Where(p => p.Module == m.Code).Select(p => p.Action).Order(StringComparer.Ordinal).ToList(),
                m.Standard.Select(a => new GroupActionInfo(a, StandardActions.Label(a), a == StandardActions.Approve ? "Aprovar aqui significa: " + m.ApproveMeaning + "." : string.Empty, true))
                    .Concat(m.Specific.Select(s => new GroupActionInfo(s.Code, s.Label, s.Description, false))).ToList(),
                m.ApproveMeaning))
            .ToList();
        return new GroupDetail(Summary(group, members.Count), members, rows);
    }

    private async Task<(HashSet<string> Enabled, HashSet<(string Module, string Action)> Actions)> GrantsAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var enabled = (await db.GroupModules.Where(m => m.GroupId == groupId && m.Enabled).Select(m => m.ModuleCode).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var actions = (await db.GroupPermissions.Where(p => p.GroupId == groupId).Select(p => new { p.ModuleCode, p.Action }).ToListAsync(cancellationToken))
            .Where(p => enabled.Contains(p.ModuleCode)).Select(p => (p.ModuleCode, p.Action)).ToHashSet();
        return (enabled, actions);
    }

    private async Task<HashSet<string>> PermissionsOfGroupAsync(Group group, CancellationToken cancellationToken)
    {
        if (group.IsOwner)
        {
            return ModuleCatalog.AllPermissions.ToHashSet(StringComparer.Ordinal);
        }

        var (_, actions) = await GrantsAsync(group.Id, cancellationToken);
        return actions.Select(a => a.Module + "." + a.Action).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>ADR 0008: toda mudança de grupo, permissão ou vínculo é notificada aos membros do Proprietário.</summary>
    private async Task NotifyOwnersAsync(Guid groupId, string title, Guid actorId, CancellationToken cancellationToken)
    {
        var owners = await db.UserGroups
            .Join(db.Groups.Where(g => g.IsSystem && g.SeedKey == Group.OwnerSeedKey), ug => ug.GroupId, g => g.Id, (ug, _) => ug.UserId)
            .Where(u => u != actorId)
            .ToListAsync(cancellationToken);
        foreach (var owner in owners)
        {
            notifications.Notify(owner, "group_changed", title, "/acessos/grupos/" + groupId);
        }
    }

    private void Audit(Guid groupId, string action, Guid? actorId, object changes) =>
        db.AuditEntries.Add(new AuditEntry { Entity = "Group", EntityId = groupId.ToString(), Action = action, UserId = actorId, At = clock.GetUtcNow(), Changes = JsonSerializer.Serialize(changes) });

    private static GroupSummary Summary(Group g, int members) => new(g.Id, g.Name, g.Description, g.IsSystem, g.IsActive, g.RequiresTwoFactor, members);

    private static Error NotFound() => Error.NotFound(Error.Codes.NotFound, "Grupo não encontrado.");

    private static Error SystemGroup() => Error.Conflict("system_group", SystemGroupMessage);
}
