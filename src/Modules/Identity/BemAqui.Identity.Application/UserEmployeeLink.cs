using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>
/// <see cref="IUserEmployeeLink"/>: grava o <c>employee_id</c> do usuário (auditado no Identity) e recusa vínculo duplo (409). Trocar o
/// vínculo apaga o PIN do usuário em todos os aparelhos e revoga as sessões por PIN dele (ADR 0026 §2, PRD-03b CA 19).
/// </summary>
public sealed class UserEmployeeLink(IIdentityDb db, Shopfloor.ShopfloorRevocations revocations) : IUserEmployeeLink
{
    public async Task<LinkableUser?> UserOfEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(u => u.EmployeeId == employeeId).Select(u => new LinkableUser(u.Id, u.Name, u.Email)).FirstOrDefaultAsync(cancellationToken);

    public Task<Guid?> EmployeeOfUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().Where(u => u.Id == userId && u.Status == UserStatus.Active).Select(u => u.EmployeeId).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<LinkableUser>> LinkableAsync(CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.EmployeeId == null).OrderBy(u => u.Name)
            .Select(u => new LinkableUser(u.Id, u.Name, u.Email)).ToListAsync(cancellationToken);

    public async Task<Result<LinkableUser>> LinkAsync(Guid userId, Guid employeeId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            return Error.Validation("validation", "Escolha um usuário ativo da lista.", new Dictionary<string, string[]> { ["userId"] = ["Escolha um usuário ativo da lista."] });
        }

        if (user.EmployeeId is { } other && other != employeeId)
        {
            return Error.Conflict("user_already_linked", "Este usuário já está ligado a outro funcionário.");
        }

        if (await db.Users.AnyAsync(u => u.EmployeeId == employeeId && u.Id != userId, cancellationToken))
        {
            return Error.Conflict("employee_already_linked", "Este funcionário já tem um usuário vinculado. Desvincule antes.");
        }

        var changed = user.EmployeeId != employeeId;
        user.EmployeeId = employeeId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("employee_already_linked", "Este funcionário já tem um usuário vinculado. Desvincule antes.");
        }

        if (changed)
        {
            await revocations.EraseUserPinsAsync(user.Id, "employee_link_changed", cancellationToken);
        }

        return new LinkableUser(user.Id, user.Name, user.Email);
    }

    public async Task<Result> UnlinkAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.EmployeeId == employeeId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("employee_not_linked", "O funcionário não tem usuário vinculado.");
        }

        user.EmployeeId = null;
        await db.SaveChangesAsync(cancellationToken);
        await revocations.EraseUserPinsAsync(user.Id, "employee_link_changed", cancellationToken);
        return Result.Success();
    }
}
