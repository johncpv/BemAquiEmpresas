using System.Text.Json;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed record GroupChip(Guid Id, string Name);

/// <summary>
/// Linha da aba Usuários (protótipo): grupos, 2FA (<c>active</c>, <c>pending</c>, <c>required_pending</c>), último acesso e situação. O
/// funcionário vinculado (USR-01 CA 6) vem com o nome e se está desligado; sem vínculo, o usuário é externo (ex.: a contadora).
/// </summary>
public sealed record UserRow(
    Guid Id, string Name, string Email, IReadOnlyList<GroupChip> Groups, string TwoFactor, DateTimeOffset? LastLoginAt, string Status, bool InviteOpen,
    DateTimeOffset? AnonymizedAt = null, string? AnonymizationRequest = null, Guid? EmployeeId = null, string? EmployeeName = null, bool EmployeeTerminated = false);

/// <summary>Convite (FND-06c); <c>EmployeeId</c> opcional liga o usuário ao funcionário já na criação (USR-01 CA 1 a 4).</summary>
public sealed record InviteUserRequest(string Name, string Email, IReadOnlyList<Guid>? GroupIds, Guid? EmployeeId = null);

public sealed record UserInvited(Guid Id, string Email);

/// <summary>
/// Usuários do tenant (FND-06c): convite com grupos sob a delegação da ADR 0008, reenvio e cancelamento, desativar sem
/// apagar histórico e reativar. Toda mudança sobe a versão de permissões do usuário (vale no próximo request).
/// </summary>
public sealed partial class UserService(
    IIdentityDb db, Invites invites, GroupService groups, UserLifecycle lifecycle, IPermissionService permissions, TwoFactorRequirement twoFactor,
    IPermissionChecker checker, IEmployees employees, TimeProvider clock)
{
    public const string OwnerNeedsActiveMember = "O grupo Proprietário precisa ter ao menos um membro ativo.";
    public const string EmployeeAlreadyLinkedMessage = "Este funcionário já tem um usuário vinculado. Desvincule antes.";

    public async Task<IReadOnlyList<UserRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Name).ThenBy(u => u.Email).ToListAsync(cancellationToken);
        var memberships = await db.UserGroups.Join(db.Groups, ug => ug.GroupId, g => g.Id, (ug, g) => new { ug.UserId, g.Id, g.Name, g.IsActive }).ToListAsync(cancellationToken);
        var openInvites = await db.UserInvites.Where(i => i.AcceptedAt == null && i.CanceledAt == null).Select(i => new { i.UserId, i.GroupIds }).ToListAsync(cancellationToken);
        var open = openInvites.Select(i => i.UserId).ToHashSet();
        var invitedGroups = openInvites.SelectMany(i => i.GroupIds.Select(g => (i.UserId, GroupId: g))).ToList();
        var groupNames = invitedGroups.Count == 0 ? new Dictionary<Guid, string>()
            : await db.Groups.Where(g => g.IsActive).ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);
        var linked = users.Where(u => u.EmployeeId is not null).Select(u => u.EmployeeId!.Value).ToList();
        var staff = linked.Count == 0 ? new Dictionary<Guid, EmployeeSummary>() : (await employees.FindAsync(linked, cancellationToken)).ToDictionary(e => e.Id);
        var rows = new List<UserRow>(users.Count);
        foreach (var user in users)
        {
            // Convite pendente: os grupos do convite só viram vínculo no aceite, mas já aparecem (e podem ser editados) na lista.
            var chips = memberships.Where(m => m.UserId == user.Id && m.IsActive).Select(m => new GroupChip(m.Id, m.Name))
                .Concat(invitedGroups.Where(i => i.UserId == user.Id && groupNames.ContainsKey(i.GroupId)).Select(i => new GroupChip(i.GroupId, groupNames[i.GroupId])))
                .DistinctBy(c => c.Id).ToList();
            var tfa = user.TotpEnabled ? "active" : user.Status == UserStatus.Active && await twoFactor.IsRequiredAsync(user, cancellationToken) ? "required_pending" : "pending";
            var employee = user.EmployeeId is { } employeeId ? staff.GetValueOrDefault(employeeId) : null;
            rows.Add(new UserRow(user.Id, user.Name, user.Email, chips, tfa, user.LastLoginAt, user.Status.ToString(), open.Contains(user.Id), user.AnonymizedAt, user.AnonymizationRequest,
                user.EmployeeId, employee?.Name, employee is { Status: EmployeeSituation.Terminated }));
        }

        return rows;
    }

    /// <summary>
    /// "Enviar convite" (CA 5, 6, 8): e-mail já cadastrado é informado; grupos passam pela delegação. Com funcionário (USR-01 CA 1 a 4): exige
    /// também <c>hr.edit</c>, o funcionário ativo e sem usuário; o vínculo nasce com o usuário convidado e reserva o funcionário.
    /// </summary>
    public async Task<Result<UserInvited>> InviteAsync(InviteUserRequest request, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var email = request.Email?.Trim() ?? string.Empty;
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Error.Validation(Error.Codes.Validation, "Informe nome e e-mail válidos.", new Dictionary<string, string[]> { ["email"] = ["Informe um e-mail válido."] });
        }

        var normalized = User.Normalize(email);
        if (await db.Users.Where(u => u.NormalizedEmail == normalized).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(cancellationToken) is { } existing)
        {
            return Error.Conflict("user_exists", "Já existe um usuário com esse e-mail. Abra o cadastro dele para ajustar os grupos.") with
            {
                Fields = new Dictionary<string, string[]> { ["userId"] = [existing.ToString()] },
            };
        }

        var groupIds = request.GroupIds ?? [];
        if (await groups.GroupGrantDeniedAsync(actorId, groupIds, cancellationToken) is { } denied)
        {
            return denied;
        }

        if (request.EmployeeId is { } employeeId && await EmployeeLinkErrorAsync(employeeId, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        Guid? id;
        try
        {
            id = await invites.EnsureInviteAsync(email, isOwner: false, invitedBy: actorId, groupIds, request.Name.Trim(), request.EmployeeId, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Corrida com outro vínculo do mesmo funcionário: o índice único de employee_id decide.
            return Error.Conflict("employee_already_linked", EmployeeAlreadyLinkedMessage);
        }

        await invites.SendAsync(email, await NameOfAsync(actorId, cancellationToken), cancellationToken);
        Audit(id!.Value, "UserInvited", actorId, new { email, groups = groupIds, employeeId = request.EmployeeId });
        await db.SaveChangesAsync(cancellationToken);
        return new UserInvited(id.Value, email);
    }

    /// <summary>"Reenviar convite" (CA 7): token novo, o anterior deixa de valer; se o convite foi cancelado, abre outro.</summary>
    public async Task<Result> ResendInviteAsync(Guid userId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null || user.Status != UserStatus.Invited)
        {
            return Error.Validation("user_not_invited", "Só dá para reenviar convite de quem ainda não entrou.");
        }

        var last = await db.UserInvites.Where(i => i.UserId == userId).OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        await invites.EnsureInviteAsync(user.Email, last?.IsOwner ?? false, last?.InvitedBy ?? actorId, last?.GroupIds, cancellationToken: cancellationToken);
        await invites.SendAsync(user.Email, await NameOfAsync(actorId, cancellationToken), cancellationToken);
        Audit(userId, "InviteResent", actorId, new { });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> CancelInviteAsync(Guid userId, Guid actorId, CancellationToken cancellationToken = default)
    {
        var open = await db.UserInvites.Where(i => i.UserId == userId && i.AcceptedAt == null && i.CanceledAt == null).ToListAsync(cancellationToken);
        if (open.Count == 0)
        {
            return Error.Validation("no_open_invite", "Não há convite pendente para este usuário.");
        }

        foreach (var invite in open)
        {
            invite.CanceledAt = clock.GetUtcNow();
        }

        Audit(userId, "InviteCanceled", actorId, new { });
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>USR-01 CA 2 a 4: vincular no convite exige <c>hr.edit</c> (como na ficha), funcionário ativo e ainda sem usuário.</summary>
    private async Task<Error?> EmployeeLinkErrorAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        if (!await checker.HasPermissionAsync("hr", "edit", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Vincular o convite a um funcionário exige também editar funcionários (RH).");
        }

        var found = await employees.FindAsync([employeeId], cancellationToken);
        if (found.Count == 0 || !found[0].IsActive)
        {
            return Error.Validation(Error.Codes.Validation, "Escolha um funcionário ativo da lista.", new Dictionary<string, string[]> { ["employeeId"] = ["Escolha um funcionário ativo da lista."] });
        }

        return await db.Users.AnyAsync(u => u.EmployeeId == employeeId, cancellationToken)
            ? Error.Conflict("employee_already_linked", EmployeeAlreadyLinkedMessage)
            : null;
    }

    private async Task<string?> NameOfAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.Id == userId).Select(u => u.Name).FirstOrDefaultAsync(cancellationToken);

    private void Audit(Guid userId, string action, Guid actorId, object changes) =>
        db.AuditEntries.Add(new AuditEntry { Entity = "User", EntityId = userId.ToString(), Action = action, UserId = actorId, At = clock.GetUtcNow(), Changes = JsonSerializer.Serialize(changes) });
}
