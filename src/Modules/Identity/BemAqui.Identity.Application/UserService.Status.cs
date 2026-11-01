using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed partial class UserService
{
    /// <summary>"Desativar" (CA 9, 10): perde o acesso no próximo request; o histórico continua com o nome dele.</summary>
    public async Task<Result> DeactivateAsync(Guid userId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Usuário não encontrado.");
        }

        if (await IsOwnerMemberAsync(userId, cancellationToken))
        {
            if (!(await permissions.GetAsync(actorId, cancellationToken)).IsOwner)
            {
                return Error.Forbidden(GroupService.DelegationDeniedCode, "Só quem é do grupo Proprietário pode desativar outro Proprietário.");
            }

            if (await ActiveOwnersAsync(except: userId, cancellationToken) == 0)
            {
                return Error.Conflict("owner_needs_member", OwnerNeedsActiveMember);
            }
        }

        var result = await lifecycle.DeactivateAsync(userId, actorId, cancellationToken);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    /// <summary>"Reativar" (CA 11): volta a ativo com os mesmos grupos. Quem nunca entrou precisa de convite novo.</summary>
    public async Task<Result> ReactivateAsync(Guid userId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Usuário não encontrado.");
        }

        if (user.AnonymizedAt is not null)
        {
            return Error.Conflict("user_anonymized", "Usuário anonimizado a pedido do titular: não pode ser reativado. Convide a pessoa de novo, com outro cadastro.");
        }

        if (user.Status != UserStatus.Inactive || user.PasswordHash is null)
        {
            return Error.Validation("user_not_inactive", "Só dá para reativar quem já entrou e foi desativado. Para quem não entrou, reenvie o convite.");
        }

        if (await IsOwnerMemberAsync(userId, cancellationToken) && !(await permissions.GetAsync(actorId, cancellationToken)).IsOwner)
        {
            return Error.Forbidden(GroupService.DelegationDeniedCode, "Só quem é do grupo Proprietário pode reativar outro Proprietário.");
        }

        user.Activate();
        Audit(userId, "Reactivated", actorId, new { before = "Inactive", after = "Active" });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Grupos do usuário: incluir passa pela delegação; tirar do Proprietário exige Proprietário e outro membro ativo.</summary>
    public async Task<Result> SetGroupsAsync(Guid userId, IReadOnlyCollection<Guid> groupIds, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        if (!await db.Users.AnyAsync(u => u.Id == userId, cancellationToken))
        {
            return Error.NotFound(Error.Codes.NotFound, "Usuário não encontrado.");
        }

        var current = await db.UserGroups.Where(ug => ug.UserId == userId).ToListAsync(cancellationToken);
        var wanted = groupIds.ToHashSet();
        var added = wanted.Except(current.Select(c => c.GroupId)).ToList();
        var removed = current.Where(c => !wanted.Contains(c.GroupId)).ToList();
        var actor = await permissions.GetAsync(actorId, cancellationToken);
        var touched = added.Concat(removed.Select(r => r.GroupId)).ToList();
        if (!actor.IsOwner && await db.UserGroups.AnyAsync(ug => ug.UserId == actorId && touched.Contains(ug.GroupId), cancellationToken))
        {
            return Error.Forbidden(GroupService.DelegationDeniedCode, "Você não pode alterar um grupo do qual faz parte. Peça a alguém do grupo Proprietário.");
        }

        if (await groups.GroupGrantDeniedAsync(actorId, added, cancellationToken) is { } denied)
        {
            return denied;
        }

        var ownerGroup = await db.Groups.Where(g => g.IsSystem && g.SeedKey == Group.OwnerSeedKey).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(cancellationToken);
        if (removed.Any(r => r.GroupId == ownerGroup))
        {
            if (!actor.IsOwner)
            {
                return Error.Forbidden(GroupService.DelegationDeniedCode, "Só quem é do grupo Proprietário pode tirar alguém dele.");
            }

            if (await ActiveOwnersAsync(except: userId, cancellationToken) == 0)
            {
                return Error.Conflict("owner_needs_member", OwnerNeedsActiveMember);
            }
        }

        db.UserGroups.RemoveRange(removed);
        db.UserGroups.AddRange(added.Select(g => new UserGroup { UserId = userId, GroupId = g }));
        Audit(userId, "GroupsChanged", actorId, new { before = current.Select(c => c.GroupId), after = wanted });
        await db.SaveChangesAsync(cancellationToken);
        await permissions.BumpAsync([userId], cancellationToken);
        return Result.Success();
    }

    private async Task<bool> IsOwnerMemberAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.UserGroups.Where(ug => ug.UserId == userId)
            .Join(db.Groups.Where(g => g.IsSystem && g.SeedKey == Group.OwnerSeedKey), ug => ug.GroupId, g => g.Id, (ug, _) => ug)
            .AnyAsync(cancellationToken);

    private async Task<int> ActiveOwnersAsync(Guid except, CancellationToken cancellationToken) =>
        await db.UserGroups.Where(ug => ug.UserId != except)
            .Join(db.Groups.Where(g => g.IsSystem && g.SeedKey == Group.OwnerSeedKey), ug => ug.GroupId, g => g.Id, (ug, _) => ug.UserId)
            .Join(db.Users.Where(u => u.Status == UserStatus.Active), id => id, u => u.Id, (id, _) => id)
            .CountAsync(cancellationToken);
}
