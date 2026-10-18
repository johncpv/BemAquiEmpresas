using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>Estado do convite, em texto no JSON (contrato estável com o front).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<InviteState>))]
public enum InviteState
{
    Valid,
    Expired,
    Accepted,
    NotFound,
}

/// <summary>O que a tela de aceite mostra: o estado, o e-mail (pré-preenchido) e o nome sugerido.</summary>
public sealed record InviteInfo(InviteState State, string? Email, string? Name, string? Message);

/// <summary>Resultado do aceite: o e-mail para o login já vir preenchido e a oferta de 2FA como próximo passo.</summary>
public sealed record InviteAccepted(Guid UserId, string Email, bool OfferTwoFactor);

/// <summary>Convites (FND-04 CA 17 a 19): criar, enviar, consultar e aceitar. O token só existe no e-mail.</summary>
public sealed class Invites(IIdentityDb db, IPasswordHasher<User> hasher, IEmailSender email, IEventPublisher events, TenantLinks links, GroupService groups, TimeProvider clock)
{
    public const string ExpiredMessage = "Este convite expirou. Peça um novo ao administrador da empresa.";
    public const string AcceptedMessage = "Este convite já foi usado. Entre com seu e-mail e senha.";
    public const string NotFoundMessage = "Convite não encontrado. Confira o link do e-mail ou peça um novo ao administrador da empresa.";

    public async Task<InviteInfo> GetAsync(string token, CancellationToken cancellationToken = default)
    {
        var (invite, user) = await FindAsync(token, cancellationToken);
        var state = StateOf(invite, user);
        return state == InviteState.Valid
            ? new InviteInfo(state, invite!.Email, user!.Name, null)
            : new InviteInfo(state, null, null, MessageOf(state));
    }

    public async Task<Result<InviteAccepted>> AcceptAsync(string token, string? name, string password, string? ip, CancellationToken cancellationToken = default)
    {
        var (invite, user) = await FindAsync(token, cancellationToken);
        var state = StateOf(invite, user);
        if (state != InviteState.Valid)
        {
            return Error.Validation("auth.invite_" + state.ToString().ToLowerInvariant(), MessageOf(state)!);
        }

        if (PasswordPolicy.Validate(password, invite!.Email) is { } problem)
        {
            return Error.Validation("auth.weak_password", problem, new Dictionary<string, string[]> { ["password"] = [problem] });
        }

        var now = clock.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(name))
        {
            user!.Name = name.Trim();
        }

        user!.SetPassword(hasher.HashPassword(user, password));
        user.Activate();
        invite.AcceptedAt = now;
        if (invite.IsOwner && await db.Groups.FirstOrDefaultAsync(g => g.IsSystem && g.SeedKey == Group.OwnerSeedKey, cancellationToken) is { } owner
            && !await db.UserGroups.AnyAsync(ug => ug.UserId == user.Id && ug.GroupId == owner.Id, cancellationToken))
        {
            db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = owner.Id }); // proprietário do provisionamento
        }

        // Grupos do convite: a delegação é conferida de novo no aceite; grupo que o autor não pode mais conceder fica de fora.
        foreach (var groupId in invite.GroupIds.Distinct())
        {
            if (invite.InvitedBy is { } inviter && await groups.GroupGrantDeniedAsync(inviter, [groupId], cancellationToken) is null)
            {
                db.UserGroups.Add(new UserGroup { UserId = user.Id, GroupId = groupId });
            }
        }

        db.AuditEntries.Add(new AuditEntry { Entity = "Auth", EntityId = user.Id.ToString(), Action = "InviteAccepted", UserId = user.Id, At = now, Ip = ip, Changes = "{}" });
        events.Publish(new UserActivated(user.Id, invite.IsOwner));
        await db.SaveChangesAsync(cancellationToken);
        return new InviteAccepted(user.Id, user.Email, OfferTwoFactor: !user.TotpEnabled);
    }

    /// <summary>
    /// Garante o usuário (status Convidado) e um convite pendente. Idempotente: rodar de novo não duplica.
    /// Usuário já ativo não recebe convite. O token só nasce no envio.
    /// </summary>
    public async Task<Guid?> EnsureInviteAsync(string emailAddress, bool isOwner, Guid? invitedBy, IReadOnlyCollection<Guid>? groupIds = null, string? name = null, CancellationToken cancellationToken = default)
    {
        var normalized = User.Normalize(emailAddress);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellationToken);
        if (user is null)
        {
            user = User.Invite(emailAddress, name);
            db.Users.Add(user);
        }
        else if (user.Status != UserStatus.Invited)
        {
            return null;
        }

        var pending = await db.UserInvites.AnyAsync(i => i.UserId == user.Id && i.AcceptedAt == null && i.CanceledAt == null, cancellationToken);
        if (!pending)
        {
            var now = clock.GetUtcNow();
            db.UserInvites.Add(new UserInvite { UserId = user.Id, Email = user.Email, IsOwner = isOwner, InvitedBy = invitedBy, GroupIds = groupIds?.ToArray() ?? [], ExpiresAt = now, CreatedAt = now });
        }

        await db.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    /// <summary>
    /// Gera um token novo para o convite pendente (o anterior deixa de valer) e enfileira o e-mail com o link no
    /// host primário do tenant (CA 15). Devolve false se não há convite pendente.
    /// </summary>
    public async Task<bool> SendAsync(string emailAddress, string? inviterName, CancellationToken cancellationToken = default)
    {
        var normalized = User.Normalize(emailAddress);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized && u.Status == UserStatus.Invited, cancellationToken);
        var invite = user is null
            ? null
            : await db.UserInvites.Where(i => i.UserId == user.Id && i.AcceptedAt == null && i.CanceledAt == null).OrderByDescending(i => i.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (invite is null)
        {
            return false;
        }

        var token = OpaqueToken.New();
        invite.TokenHash = OpaqueToken.Hash(token);
        invite.ExpiresAt = clock.GetUtcNow() + UserInvite.Lifetime;
        email.Enqueue("invite", invite.Email, new Dictionary<string, object?>
        {
            ["link"] = links.Absolute("/convite?token=" + Uri.EscapeDataString(token)),
            ["expires_at"] = invite.ExpiresAt,
            ["recipient_name"] = string.IsNullOrEmpty(user!.Name) ? null : user.Name,
            ["inviter_name"] = inviterName,
        }, "UserInvite", invite.Id);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<(UserInvite? Invite, User? User)> FindAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token))
        {
            return (null, null);
        }

        var hash = OpaqueToken.Hash(token);
        var invite = await db.UserInvites.FirstOrDefaultAsync(i => i.TokenHash == hash, cancellationToken);
        var user = invite is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == invite.UserId, cancellationToken);
        return (invite, user);
    }

    private InviteState StateOf(UserInvite? invite, User? user) =>
        invite is null || user is null || user.Status == UserStatus.Inactive || invite.CanceledAt is not null ? InviteState.NotFound
        : invite.AcceptedAt is not null || user.Status == UserStatus.Active ? InviteState.Accepted
        : invite.ExpiresAt <= clock.GetUtcNow() ? InviteState.Expired
        : InviteState.Valid;

    private static string? MessageOf(InviteState state) => state switch
    {
        InviteState.Expired => ExpiredMessage,
        InviteState.Accepted => AcceptedMessage,
        InviteState.NotFound => NotFoundMessage,
        _ => null,
    };
}
