using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>Código de recuperação do 2FA (<c>identity.recovery_codes</c>): uso único, guardado só como hash (FND-05 CA 7).</summary>
public sealed class RecoveryCode : Entity
{
    public const int CountPerUser = 10;

    public Guid UserId { get; init; }

    public required string CodeHash { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? UsedAt { get; set; }
}

public enum TwoFactorChallengePurpose
{
    /// <summary>Senha certa, falta o código (FND-05 CA 3 e 4).</summary>
    Verify,

    /// <summary>Senha certa, 2FA obrigatório e ainda não configurado: só libera a configuração (CA 8).</summary>
    Setup,
}

/// <summary>
/// Desafio de 2FA (<c>identity.two_factor_challenges</c>): token opaco de 5 min, guardado como hash. Não é um bearer
/// token, então não abre nenhuma rota além das de 2FA. Cinco erros invalidam o desafio (CA 6).
/// </summary>
public sealed class TwoFactorChallenge : Entity
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public Guid UserId { get; init; }

    public required string TokenHash { get; init; }

    public TwoFactorChallengePurpose Purpose { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public int Attempts { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? InvalidatedAt { get; private set; }

    public string? CreatedByIp { get; init; }

    public bool IsOpen(DateTimeOffset now) => CompletedAt is null && InvalidatedAt is null && ExpiresAt > now;

    public int RemainingAttempts => Math.Max(0, MaxAttempts - Attempts);

    /// <summary>Código errado: devolve true se esta foi a 5ª tentativa e o desafio caiu.</summary>
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

    public void Complete(DateTimeOffset now) => CompletedAt ??= now;
}

/// <summary>Configuração do tenant (<c>identity.tenant_settings</c>), chave/valor auditado. Ex.: <c>security.require_2fa</c>.</summary>
public sealed class TenantSetting : Entity, IAuditable
{
    public const string RequireTwoFactor = "security.require_2fa";

    public required string Key { get; init; }

    public string Value { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
