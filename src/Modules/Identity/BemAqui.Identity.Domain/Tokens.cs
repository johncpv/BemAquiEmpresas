using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>
/// Refresh token rotativo (<c>identity.refresh_tokens</c>), guardado só como hash. Cada renovação gera um filho
/// na mesma família; reapresentar um já substituído fora da tolerância revoga a família inteira (ADR 0008).
/// </summary>
public sealed class RefreshToken : Entity
{
    public Guid UserId { get; init; }

    public required string TokenHash { get; init; }

    public Guid FamilyId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? ReplacedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? CreatedByIp { get; init; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ReplacedAt is null && ExpiresAt > now;

    public void ReplaceWith(Guid next, DateTimeOffset now)
    {
        ReplacedAt ??= now;
        ReplacedById ??= next;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}

/// <summary>Convite para entrar no tenant (<c>identity.user_invites</c>): token de uso único guardado só como hash.</summary>
public sealed class UserInvite : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    public Guid UserId { get; init; }

    [PersonalData]
    public required string Email { get; init; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public Guid? InvitedBy { get; init; }

    /// <summary>Grupos do convite: validados pela delegação no envio e de novo no aceite (ADR 0008).</summary>
    public Guid[] GroupIds { get; set; } = [];

    /// <summary>Cancelado pelo administrador: o link deixa de valer.</summary>
    public DateTimeOffset? CanceledAt { get; set; }

    /// <summary>Convite do proprietário criado no provisionamento (entra no grupo Proprietário na FND-06b).</summary>
    public bool IsOwner { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Pedido de redefinição de senha: token de uso único, válido por 1 h (FND-04 CA 13).</summary>
public sealed class PasswordReset : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public Guid UserId { get; init; }

    public required string TokenHash { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? UsedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
