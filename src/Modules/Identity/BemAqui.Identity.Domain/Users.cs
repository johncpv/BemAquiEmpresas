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

    /// <summary>Segredo TOTP cifrado (<c>SecretKind.TotpSecret</c>); existe desde o início da ativação, nunca volta para o cliente.</summary>
    [ProtectedSecret]
    public string? TotpSecretCipher { get; private set; }

    /// <summary>Último intervalo de 30 s aceito: um código do mesmo intervalo (ou anterior) é recusado (anti-replay).</summary>
    public long? TotpLastStep { get; private set; }

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

    /// <summary>Senha certa, mas ainda falta o 2FA: zera as falhas sem contar como entrada.</summary>
    public void RegisterPasswordVerified()
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset now)
    {
        AccessFailedCount = 0;
        LockoutEnd = null;
        LastLoginAt = now;
    }

    /// <summary>Começa (ou recomeça) a ativação: o 2FA só liga em <see cref="EnableTotp"/>.</summary>
    public void BeginTotpSetup(string secretCipher)
    {
        if (TotpEnabled)
        {
            throw new InvalidOperationException("O 2FA já está ativo.");
        }

        TotpSecretCipher = secretCipher;
        TotpLastStep = null;
    }

    public void EnableTotp(long acceptedStep)
    {
        TotpEnabled = true;
        TotpLastStep = acceptedStep;
        RotateSecurityStamp();
    }

    public void DisableTotp()
    {
        TotpEnabled = false;
        TotpSecretCipher = null;
        TotpLastStep = null;
        RotateSecurityStamp();
    }

    public void RotateSecurityStamp() => SecurityStamp = NewStamp();

    private static string NewStamp() => Guid.NewGuid().ToString("N");
}
