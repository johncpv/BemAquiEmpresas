using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>
/// <see cref="IUserEmployeeLink"/>: grava o <c>employee_id</c> do usuário (auditado no Identity) e recusa vínculo duplo (409). Trocar o
/// vínculo apaga o PIN do usuário em todos os aparelhos e revoga as sessões por PIN dele (ADR 0026 §2, PRD-03b CA 19). O desligamento
/// desativa o login pelo <see cref="UserService.DeactivateAsync"/> (USR-01), com as mesmas regras do Proprietário.
/// </summary>
public sealed class UserEmployeeLink(IIdentityDb db, Shopfloor.ShopfloorRevocations revocations, UserService users, ICurrentUser currentUser, TimeProvider clock) : IUserEmployeeLink
{
    public async Task<LinkableUser?> UserOfEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(u => u.EmployeeId == employeeId).Select(u => new LinkableUser(u.Id, u.Name, u.Email, u.Status.ToString())).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyCollection<Guid>> LinkedEmployeeIdsAsync(CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking().Where(u => u.EmployeeId != null).Select(u => u.EmployeeId!.Value).ToListAsync(cancellationToken);

    public async Task<TerminationLogin> DeactivateForTerminationAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().Where(u => u.EmployeeId == employeeId).Select(u => new { u.Id, u.Name, u.Email, u.Status }).FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return TerminationLogin.None;
        }

        var name = string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name;
        if (user.Status == UserStatus.Inactive)
        {
            return new TerminationLogin(TerminationLoginOutcome.AlreadyInactive, user.Id, name, $"O login de {name} já estava desativado.");
        }

        // O mesmo caminho do "Desativar" da tela de usuários: delegação do Proprietário, último membro ativo, sessões derrubadas.
        var result = await users.DeactivateAsync(user.Id, currentUser.UserId ?? Guid.Empty, cancellationToken);
        var login = result.IsSuccess
            ? new TerminationLogin(TerminationLoginOutcome.Deactivated, user.Id, name, $"O login de {name} foi desativado.")
            : result.Error!.Code == "owner_needs_member"
                ? new TerminationLogin(TerminationLoginOutcome.KeptLastOwner, user.Id, name,
                    $"O login de {name} continua ativo: é o único membro ativo do grupo Proprietário. Inclua outra pessoa no Proprietário e desative o login em Usuários.")
                : new TerminationLogin(TerminationLoginOutcome.KeptOwner, user.Id, name,
                    $"O login de {name} continua ativo: é do grupo Proprietário, e só quem é do Proprietário pode desativá-lo.");
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = "User",
            EntityId = user.Id.ToString(),
            Action = login.Outcome == TerminationLoginOutcome.Deactivated ? "DeactivatedByTermination" : "TerminationDeactivationSkipped",
            UserId = currentUser.UserId,
            At = clock.GetUtcNow(),
            Ip = currentUser.IpAddress,
            Changes = JsonSerializer.Serialize(new { employeeId, outcome = login.Outcome.ToString() }),
        });
        await db.SaveChangesAsync(cancellationToken);
        return login;
    }

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
