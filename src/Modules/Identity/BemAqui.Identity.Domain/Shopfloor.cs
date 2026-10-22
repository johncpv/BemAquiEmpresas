using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>Tema do <c>/fab</c> guardado no aparelho (ponto 95): vale para todos os operadores daquele tablet.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceTheme>))]
public enum DeviceTheme
{
    System,
    Light,
    Dark,
}

/// <summary>Como o segredo apresentado pelo cookie casa com o aparelho (ADR 0026 §1, segredo rotativo).</summary>
public enum DeviceSecretMatch
{
    None,
    Current,

    /// <summary>O anterior, ainda dentro da tolerância da rotação (a resposta com o novo pode não ter chegado).</summary>
    PreviousInTolerance,

    /// <summary>O anterior, fora da tolerância: sinal de cópia do cookie; o aparelho é revogado.</summary>
    Reused,
}

/// <summary>
/// Aparelho da fábrica registrado (<c>identity.shopfloor_devices</c>, ADR 0026 §1): segredo opaco de 32 bytes no cookie
/// <c>__Host-bq_dev</c>, só o hash no banco, rotativo a cada login por PIN; vale 180 dias e cai depois de 30 dias sem uso.
/// </summary>
public sealed class ShopfloorDevice : Entity, IAuditable
{
    public const int MaxNameLength = 40;
    public const int DefaultMaxActive = 10;
    public const int DeviceFailureLimit = 20;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);
    public static readonly TimeSpan UnusedLimit = TimeSpan.FromDays(30);
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    /// <summary>Uso gravado no máximo a cada hora, para a auditoria não crescer a cada renovação.</summary>
    public static readonly TimeSpan SeenGranularity = TimeSpan.FromHours(1);

    public string Name { get; set; } = string.Empty;

    [ProtectedSecret]
    public string SecretHash { get; private set; } = string.Empty;

    [ProtectedSecret]
    public string? PreviousSecretHash { get; private set; }

    public DateTimeOffset SecretRotatedAt { get; private set; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>Quem revogou; null quando foi o job (sem uso ou vencido) ou a detecção de reuso do segredo.</summary>
    public Guid? RevokedBy { get; private set; }

    /// <summary><c>manual</c>, <c>unused</c>, <c>expired</c> ou <c>secret_reused</c>.</summary>
    public string? RevokedReason { get; private set; }

    public DeviceTheme Theme { get; set; } = DeviceTheme.System;

    /// <summary>Entrada por PIN bloqueada no aparelho (20 erros em 15 min, somando todos os usuários).</summary>
    public DateTimeOffset? PinLockedUntil { get; private set; }

    public DateTimeOffset? PinFailuresSince { get; private set; }

    public int PinFailures { get; private set; }

    public static ShopfloorDevice Register(string name, string secretHash, Guid createdBy, DateTimeOffset now) => new()
    {
        Name = name,
        SecretHash = secretHash,
        SecretRotatedAt = now,
        CreatedBy = createdBy,
        CreatedAt = now,
        LastSeenAt = now,
        ExpiresAt = now + Lifetime,
    };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    /// <summary>Sem uso há 30 dias ou com os 180 dias vencidos: o job diário revoga.</summary>
    public string? StaleReason(DateTimeOffset now) =>
        RevokedAt is not null ? null : ExpiresAt <= now ? "expired" : LastSeenAt <= now - UnusedLimit ? "unused" : null;

    public DeviceSecretMatch Match(string hash, DateTimeOffset now, TimeSpan tolerance)
    {
        if (string.Equals(hash, SecretHash, StringComparison.Ordinal))
        {
            return DeviceSecretMatch.Current;
        }

        if (PreviousSecretHash is not null && string.Equals(hash, PreviousSecretHash, StringComparison.Ordinal))
        {
            return now - SecretRotatedAt <= tolerance ? DeviceSecretMatch.PreviousInTolerance : DeviceSecretMatch.Reused;
        }

        return DeviceSecretMatch.None;
    }

    /// <summary>Segredo novo a cada login por PIN; o anterior vale só dentro da tolerância.</summary>
    public void Rotate(string newHash, DateTimeOffset now)
    {
        PreviousSecretHash = SecretHash;
        SecretHash = newHash;
        SecretRotatedAt = now;
        Touch(now);
    }

    public void Touch(DateTimeOffset now)
    {
        if (now - LastSeenAt >= SeenGranularity)
        {
            LastSeenAt = now;
        }
    }

    public void Revoke(Guid? by, string reason, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedBy = by;
        RevokedReason = reason;
    }

    public bool IsPinLocked(DateTimeOffset now) => PinLockedUntil is { } until && until > now;

    /// <summary>Erro de PIN no aparelho: no 20º dentro de 15 min, bloqueia a entrada por PIN por 15 min. Devolve true se bloqueou agora.</summary>
    public bool RegisterPinFailure(DateTimeOffset now)
    {
        if (PinFailuresSince is not { } since || now - since > FailureWindow)
        {
            PinFailuresSince = now;
            PinFailures = 0;
        }

        PinFailures++;
        if (PinFailures < DeviceFailureLimit)
        {
            return false;
        }

        PinLockedUntil = now + LockDuration;
        PinFailures = 0;
        PinFailuresSince = null;
        return true;
    }
}

/// <summary>Resultado de um PIN errado no par usuário + aparelho (ADR 0026 §2).</summary>
public enum PinFailure
{
    Counted,

    /// <summary>5 erros seguidos: PIN bloqueado por 15 min.</summary>
    Locked,

    /// <summary>10º erro em 24 h: o PIN é apagado e o usuário precisa de novo login completo.</summary>
    Erased,
}

/// <summary>PIN de 6 dígitos de um usuário num aparelho (<c>identity.user_pins</c>), guardado com PBKDF2; único por usuário + aparelho.</summary>
public sealed class UserPin : Entity, IAuditable
{
    public const int Length = 6;
    public const int MaxConsecutiveFailures = 5;
    public const int DailyFailureLimit = 10;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DailyWindow = TimeSpan.FromHours(24);

    public Guid UserId { get; init; }

    public Guid DeviceId { get; init; }

    [ProtectedSecret]
    public string PinHash { get; private set; } = string.Empty;

    public int FailedCount { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset? FailuresSince { get; private set; }

    public int FailuresInWindow { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public static UserPin Create(Guid userId, Guid deviceId, string pinHash, DateTimeOffset now) => new()
    {
        UserId = userId,
        DeviceId = deviceId,
        PinHash = pinHash,
        CreatedAt = now,
    };

    /// <summary>Novo PIN no mesmo par: zera os erros e o bloqueio.</summary>
    public void Replace(string pinHash, DateTimeOffset now)
    {
        PinHash = pinHash;
        CreatedAt = now;
        FailedCount = 0;
        LockedUntil = null;
        FailuresSince = null;
        FailuresInWindow = 0;
    }

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>5 erros seguidos bloqueiam 15 min; o 10º em 24 h apaga o PIN (quem chama remove a linha).</summary>
    public PinFailure RegisterFailure(DateTimeOffset now)
    {
        if (FailuresSince is not { } since || now - since > DailyWindow)
        {
            FailuresSince = now;
            FailuresInWindow = 0;
        }

        FailuresInWindow++;
        FailedCount++;
        if (FailuresInWindow >= DailyFailureLimit)
        {
            return PinFailure.Erased;
        }

        if (FailedCount < MaxConsecutiveFailures)
        {
            return PinFailure.Counted;
        }

        LockedUntil = now + LockDuration;
        FailedCount = 0;
        return PinFailure.Locked;
    }

    public void RegisterSuccess(DateTimeOffset now)
    {
        FailedCount = 0;
        LockedUntil = null;
        LastUsedAt = now;
    }
}