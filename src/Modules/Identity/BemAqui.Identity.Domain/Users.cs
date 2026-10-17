using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

public enum UserStatus
{
    Invited,
    Active,
    Inactive,
}

/// <summary>Usuário do tenant (<c>identity.users</c>). Desativar não apaga: preserva o histórico (SEC-4).</summary>
public sealed class User : Entity, IAuditable
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    [PersonalData]
    public string Name { get; set; } = string.Empty;

    [PersonalData]
    public required string Email { get; init; }

    /// <summary>E-mail em minúsculas, para busca e unicidade.</summary>
    public required string NormalizedEmail { get; init; }

    [ProtectedSecret]
    public string? PasswordHash { get; private set; }

    /// <summary>Muda a cada evento que deve derrubar sessões (senha, desativação, 2FA): vai no token como <c>sstamp</c>.</summary>
    [ProtectedSecret]
    public string SecurityStamp { get; private set; } = NewStamp();

    public UserStatus Status { get; private set; } = UserStatus.Invited;

    public int AccessFailedCount { get; private set; }

    public DateTimeOffset? LockoutEnd { get; private set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public bool TotpEnabled { get; set; }

    public static User Invite(string email, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var trimmed = email.Trim();
        return new User { Email = trimmed, NormalizedEmail = Normalize(trimmed), Name = name ?? string.Empty };
    }

    public static string Normalize(string email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return email.Trim().ToLowerInvariant();
    }

    public bool IsLockedOut(DateTimeOffset now) => LockoutEnd is { } end && end > now;

    public void SetPassword(string hash)
    {
        PasswordHash = hash;
        RotateSecurityStamp();
    }

    public void Activate()
    {
        Status = UserStatus.Active;
        RotateSecurityStamp();
    }

    public void Deactivate()
    {
        Status = UserStatus.Inactive;
        RotateSecurityStamp();
    }

    /// <summary>Senha errada: na 5ª seguida, bloqueia por 15 min (FND-04 CA 9). Devolve true se bloqueou agora.</summary>
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

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }

    public void RotateSecurityStamp() => SecurityStamp = NewStamp();

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
