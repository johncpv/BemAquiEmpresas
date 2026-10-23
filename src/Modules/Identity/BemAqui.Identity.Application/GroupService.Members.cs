using BemAqui.BuildingBlocks.Modules;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed partial class GroupService
{
    public const string DelegationDeniedCode = "delegation_denied";

    /// <summary>
    /// Membros do grupo (CA 11, 14). Pôr alguém num grupo concede as permissões dele, então vale a delegação: o ator
    /// precisa ter todas, e o grupo Proprietário só recebe membro pelas mãos de outro Proprietário.
    /// </summary>
    public async Task<Result<GroupSaved>> SetMembersAsync(Guid id, IReadOnlyCollection<Guid> userIds, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return NotFound();
        }

        if (!group.IsOwner && await OwnGroupDeniedAsync(group.Id, actorId, cancellationToken) is { } own)
        {
            return own;
        }

        var wanted = userIds.ToHashSet();
        if (wanted.Count > 0 && await db.Users.CountAsync(u => wanted.Contains(u.Id), cancellationToken) != wanted.Count)
        {
            return Error.Validation(Error.Codes.Validation, "Há usuário que não existe nesta empresa.");
        }

        var current = await db.UserGroups.Where(ug => ug.GroupId == id).ToListAsync(cancellationToken);
        var added = wanted.Except(current.Select(c => c.UserId)).ToList();
        if (group.IsOwner && wanted.Count == 0)
        {
            return Error.Conflict("owner_needs_member", OwnerNeedsMemberMessage);
        }

        if (added.Count > 0)
        {
            var actor = await permissions.GetAsync(actorId, cancellationToken);
            if (group.IsOwner && !actor.IsOwner)
            {
                return Denied("Só quem é do grupo Proprietário pode adicionar alguém a ele.");
            }

            if (await DelegationDeniedAsync(actorId, await PermissionsOfGroupAsync(group, cancellationToken), cancellationToken) is { } denied)
            {
                return denied;
            }
        }

        var removed = current.Where(c => !wanted.Contains(c.UserId)).ToList();
        db.UserGroups.RemoveRange(removed);
        db.UserGroups.AddRange(added.Select(u => new UserGroup { UserId = u, GroupId = id }));
        Audit(id, "MembersChanged", actorId, new { before = current.Select(c => c.UserId), after = wanted });
        await NotifyOwnersAsync(id, "Membros do grupo " + group.Name + " alterados.", actorId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await permissions.BumpAsync(added.Concat(removed.Select(r => r.UserId)).ToList(), cancellationToken);

        return new GroupSaved(id, added.Count + removed.Count);
    }

    /// <summary>
    /// Pode o ator pôr alguém nestes grupos? (convite e grupos do usuário, FND-06c CA 6). Grupo inexistente ou inativo
    /// é erro de validação; o resto segue a delegação da ADR 0008.
    /// </summary>
    public async Task<Error?> GroupGrantDeniedAsync(Guid actorId, IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        var ids = groupIds.Distinct().ToList();
        var groups = await db.Groups.AsNoTracking().Where(g => ids.Contains(g.Id) && g.IsActive).ToListAsync(cancellationToken);
        if (groups.Count != ids.Count)
        {
            return Error.Validation(Error.Codes.Validation, "Há grupo que não existe ou está inativo.");
        }

        var actor = await permissions.GetAsync(actorId, cancellationToken);
        foreach (var group in groups)
        {
            if (group.IsOwner && !actor.IsOwner)
            {
                return Denied("Só quem é do grupo Proprietário pode adicionar alguém a ele.");
            }

            if (await DelegationDeniedAsync(actorId, await PermissionsOfGroupAsync(group, cancellationToken), cancellationToken) is { } denied)
            {
                return denied;
            }
        }

        return null;
    }

    /// <summary>
    /// Ações reservadas ao Proprietário (ADR 0008): <c>*.approve</c>, <c>finance.pay</c>, <c>*.view_cost</c> (D7), <c>hr.view_sensitive</c>,
    /// <c>hr.view_health</c> (adendo de 21/10/2026, CAD-04c1b), <c>identity.edit</c> e <c>settings.edit</c>.
    /// </summary>
    public static bool IsSensitive(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        var action = permission[(permission.IndexOf('.', StringComparison.Ordinal) + 1)..];
        return action is StandardActions.Approve or "pay" or "view_cost" || permission is "hr.view_sensitive" or "hr.view_health" or "identity.edit" or "settings.edit";
    }

    /// <summary>ADR 0008: ninguém altera um grupo de que é membro, a não ser o Proprietário.</summary>
    private async Task<Error?> OwnGroupDeniedAsync(Guid groupId, Guid actorId, CancellationToken cancellationToken)
    {
        if (!await db.UserGroups.AnyAsync(ug => ug.GroupId == groupId && ug.UserId == actorId, cancellationToken)
            || (await permissions.GetAsync(actorId, cancellationToken)).IsOwner)
        {
            return null;
        }

        return Denied("Você não pode alterar um grupo do qual faz parte. Peça a alguém do grupo Proprietário.");
    }

    /// <summary>Ninguém concede o que não tem; sensível só pelo Proprietário (CA 13, 14). Devolve o erro ou null.</summary>
    private async Task<Error?> DelegationDeniedAsync(Guid actorId, IEnumerable<string> granted, CancellationToken cancellationToken)
    {
        var list = granted.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var actor = await permissions.GetAsync(actorId, cancellationToken);
        if (actor.IsOwner)
        {
            return null;
        }

        var sensitive = list.FirstOrDefault(IsSensitive);
        if (sensitive is not null)
        {
            return Denied($"Só o grupo Proprietário pode conceder {Describe(sensitive)}.");
        }

        var missing = list.FirstOrDefault(p => !actor.Has(p));
        return missing is null ? null : Denied($"Você não pode conceder {Describe(missing)}, porque não tem essa permissão.");
    }

    private async Task<int> InvalidateMembersAsync(Guid groupId, CancellationToken cancellationToken)
    {
        var members = await db.UserGroups.Where(ug => ug.GroupId == groupId).Select(ug => ug.UserId).ToListAsync(cancellationToken);
        await permissions.BumpAsync(members, cancellationToken);

        return members.Count;
    }

    private static string Describe(string permission)
    {
        var dot = permission.IndexOf('.', StringComparison.Ordinal);
        var module = ModuleCatalog.Find(permission[..dot]);
        var action = permission[(dot + 1)..];
        var label = module?.Specific.FirstOrDefault(s => s.Code == action)?.Label ?? StandardActions.Label(action);
        return $"\"{label}\" em {module?.Name ?? permission[..dot]} ({permission})";
    }

    private static Error Denied(string message) => Error.Forbidden(DelegationDeniedCode, message);
}
