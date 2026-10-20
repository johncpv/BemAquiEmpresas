using BemAqui.BuildingBlocks.Identity;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Infrastructure;

internal sealed class UserDirectory(IIdentityDb db) : IUserDirectory
{
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
}
