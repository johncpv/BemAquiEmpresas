using BemAqui.SharedKernel;

namespace BemAqui.Platform.Domain;

public enum PlatformAdminStatus
{
    Invited,
    Active,
    Inactive,
}

/// <summary>
/// Super admin da plataforma (<c>catalog.platform_admins</c>, FND-13). Criado só pela CLI; 2FA sempre obrigatório.
/// Mesmas regras de senha, bloqueio e TOTP dos usuários de tenant (FND-04/05).
/// </summary>
public sealed class PlatformAdmin : Entity, IAuditable
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    [PersonalData]
    public required string Email { get; init; }

    public required string NormalizedEmail { get; init; }

    [ProtectedSecret]
    public string? PasswordHash { get; private set; }

    [ProtectedSecret]
    public string SecurityStamp { get; private set; } = NewStamp();

    public PlatformAdminStatus Status { get; private set; } = PlatformAdminStatus.Invited;

    public int AccessFailedCount { get; private set; }

    public DateTimeOffset? LockoutEnd { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public bool TotpEnabled { get; private set; }

    [ProtectedSecret]
    public string? TotpSecretCipher { get; private set; }

    public long? TotpLastStep { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public static PlatformAdmin Create(string email, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return new PlatformAdmin { Email = email.Trim(), NormalizedEmail = Normalize(email), CreatedAt = now };
    }

    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    /// <summary>Senha definida pelo link da CLI: o convidado passa a ativo (o 2FA vem no primeiro login).</summary>
    public void SetPassword(string hash)
    {
        PasswordHash = hash;
        if (Status == PlatformAdminStatus.Invited)
        {
            Status = PlatformAdminStatus.Active;
        }

        SecurityStamp = NewStamp();
    }

    public bool RegisterFailedLogin(DateTimeOffset now)
    {
        AccessFailedCount++;
        if (AccessFailedCount < MaxFailedAttempts)
        {
            return false;
        }

        LockoutEnd = now + LockoutDuration;
        AccessFailedCount = 0;
        return true;
    }

    public void RegisterPasswordVerified()
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now) => LastLoginAt = now;

    public void BeginTotpSetup(string secretCipher)
    {
        if (TotpEnabled)
        {
            throw new InvalidOperationException("O 2FA já está ativo.");
        }

        TotpSecretCipher = secretCipher;
    }

    public void EnableTotp(long acceptedStep)
    {
        TotpEnabled = true;
        TotpLastStep = acceptedStep;
        SecurityStamp = NewStamp();
    }

    public void Deactivate()
    {
        Status = PlatformAdminStatus.Inactive;
        SecurityStamp = NewStamp();
    }

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}

public enum PlatformTokenPurpose
{
    /// <summary>Link da CLI para definir a senha (24 h).</summary>
    SetPassword,

    /// <summary>Senha certa, falta o código do 2FA (5 min).</summary>
    Verify,

    /// <summary>Senha certa, 2FA ainda não configurado: só libera a configuração (5 min).</summary>
    Setup,
}

/// <summary>Token opaco de uso único (<c>catalog.platform_admin_tokens</c>), guardado como hash; desafios contam tentativas.</summary>
public sealed class PlatformAdminToken : Entity
{
    public const int MaxAttempts = 5;

    public Guid AdminId { get; init; }

    public required string TokenHash { get; init; }

    public PlatformTokenPurpose Purpose { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public int Attempts { get; private set; }

    public DateTimeOffset? UsedAt { get; private set; }

    public DateTimeOffset? InvalidatedAt { get; private set; }

    public bool IsOpen(DateTimeOffset now) => UsedAt is null && InvalidatedAt is null && ExpiresAt > now;

    public int RemainingAttempts => Math.Max(0, MaxAttempts - Attempts);

    public bool RegisterFailure(DateTimeOffset now)
    {
        Attempts++;
        if (Attempts < MaxAttempts)
        {
            return false;
        }

        InvalidatedAt ??= now;
        return true;
    }

    public void Use(DateTimeOffset now) => UsedAt ??= now;

    public void Invalidate(DateTimeOffset now) => InvalidatedAt ??= now;
}

public sealed class PlatformRecoveryCode : Entity
{
    public Guid AdminId { get; init; }

    public required string CodeHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UsedAt { get; set; }
}

/// <summary>
/// Sessão do super admin (<c>catalog.platform_sessions</c>): cookie opaco, preso ao host da plataforma, que expira
/// por inatividade e por tempo absoluto (FND-13 CA 9).
/// </summary>
public sealed class PlatformSession : Entity
{
    public Guid AdminId { get; init; }

    public required string TokenHash { get; init; }

    public required string Host { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? CreatedByIp { get; init; }

    public bool IsActive(DateTimeOffset now, TimeSpan idle, TimeSpan absolute) =>
        RevokedAt is null && LastSeenAt + idle > now && CreatedAt + absolute > now;

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
