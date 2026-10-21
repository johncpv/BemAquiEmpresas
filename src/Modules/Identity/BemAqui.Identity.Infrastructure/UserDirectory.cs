using BemAqui.BuildingBlocks.Identity;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Infrastructure;

internal sealed class UserDirectory(IIdentityDb db, IPermissionService permissions) : IUserDirectory
{
    public async Task<IReadOnlyList<Guid>> UsersWithPermissionAsync(string permission, CancellationToken cancellationToken = default)
    {
        var result = new List<Guid>();
        foreach (var user in await ActiveUserIdsAsync(cancellationToken))
        {
            if ((await permissions.GetAsync(user, cancellationToken)).Has(permission))
            {
                result.Add(user);
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<Guid>> OwnerIdsAsync(CancellationToken cancellationToken = default) =>
        await (from ug in db.UserGroups
               join g in db.Groups on ug.GroupId equals g.Id
               join u in db.Users on ug.UserId equals u.Id
               where g.IsSystem && g.SeedKey == Group.OwnerSeedKey && u.Status == UserStatus.Active
               select u.Id).Distinct().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ActiveUserIdsAsync(CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync(cancellationToken);

    public Task<string?> DisplayNameAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => (string?)u.Name).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> GroupMembersAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        await (from ug in db.UserGroups
               join u in db.Users on ug.UserId equals u.Id
               where ug.GroupId == groupId && u.Status == UserStatus.Active
               select u.Id).Distinct().ToListAsync(cancellationToken);

    public Task<Guid?> OwnerGroupIdAsync(CancellationToken cancellationToken = default) =>
        db.Groups.AsNoTracking().Where(g => g.IsSystem && g.SeedKey == Group.OwnerSeedKey).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(cancellationToken);

    public Task<string?> GroupNameAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => (string?)g.Name).FirstOrDefaultAsync(cancellationToken);

    public Task<string?> EmailOfAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId && u.Status == UserStatus.Active).Select(u => (string?)u.Email).FirstOrDefaultAsync(cancellationToken);
}
