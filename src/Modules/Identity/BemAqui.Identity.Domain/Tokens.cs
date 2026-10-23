using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>Tipo da sessão (ADR 0026 §3): completa (e-mail e senha) ou por PIN, presa a um aparelho registrado.</summary>
public enum SessionKind
{
    Full,
    Pin,
}

/// <summary>
/// Refresh token rotativo (<c>identity.refresh_tokens</c>), guardado só como hash. Cada renovação gera um filho
/// na mesma família; reapresentar um já substituído fora da tolerância revoga a família inteira (ADR 0008). A família tem prazo
/// absoluto (ADR 0026 §3): 30 dias na sessão completa e 12 h, sem renovação além disso, na sessão por PIN.
/// </summary>
public sealed class RefreshToken : Entity
{
    public static readonly TimeSpan PinSessionLifetime = TimeSpan.FromHours(12);
    public const int DefaultAbsoluteDays = 30;

    public Guid UserId { get; init; }

    public SessionKind SessionKind { get; init; } = SessionKind.Full;

    /// <summary>Aparelho da sessão por PIN; null na sessão completa.</summary>
    public Guid? DeviceId { get; init; }

    /// <summary>Fim da família: a renovação não passa daqui. Null nas famílias abertas antes da PRD-03b (conta do início delas).</summary>
    public DateTimeOffset? AbsoluteExpiresAt { get; init; }

    /// <summary>Host do tenant em que a sessão nasceu: renovar por outro host (outro domínio do tenant) responde 401.</summary>
    public string? Host { get; init; }

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
