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

/// <summary>
/// Tipo do aparelho registrado (PDV-01, ADR 0048 §1): o da fábrica (o de sempre, com PIN do /fab) e o caixa do PDV (chave ECDSA, loja, caixa e
/// época). KDS, garçom e estação de impressão entram nas Fases 12 e 15. O teto de aparelhos ativos é contado por tipo.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DeviceKind>))]
public enum DeviceKind
{
    Factory,
    Register,
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

    /// <summary><c>manual</c>, <c>unused</c>, <c>expired</c>, <c>secret_reused</c> ou <c>go_live</c> (script de acessos da virada, GOL-01a).</summary>
    public string? RevokedReason { get; private set; }

    public DeviceTheme Theme { get; set; } = DeviceTheme.System;

    /// <summary>Entrada por PIN bloqueada no aparelho (20 erros em 15 min, somando todos os usuários).</summary>
    public DateTimeOffset? PinLockedUntil { get; private set; }

    public DateTimeOffset? PinFailuresSince { get; private set; }

    public int PinFailures { get; private set; }

    /// <summary>Tipo (PDV-01): os aparelhos de antes da migration ficam como Fábrica.</summary>
    public DeviceKind Kind { get; init; } = DeviceKind.Factory;

    /// <summary>Loja do caixa (<c>pos.stores</c>, só o id); null no aparelho da fábrica.</summary>
    public Guid? StoreId { get; init; }

    /// <summary>Caixa (<c>pos.registers</c>, só o id); null no aparelho da fábrica.</summary>
    public Guid? RegisterId { get; init; }

    /// <summary>Época do caixa quando este aparelho entrou (CA 6): entra na chave dos eventos e no número da venda.</summary>
    public int RegisterEpoch { get; init; }

    /// <summary>Chave pública ECDSA P-256 do aparelho (SubjectPublicKeyInfo em base64); a privada nunca sai do aparelho.</summary>
    public string? PublicKey { get; init; }

    /// <summary>Última sincronização bem-sucedida do caixa (o segredo gira a cada uma). Fora do diff da auditoria: a sincronização é a trilha.</summary>
    [NotAudited]
    public DateTimeOffset? LastSyncAt { get; private set; }

    /// <summary>Caixa do PDV (PDV-01 CA 4): loja, caixa, época e a chave pública; mesma validade do aparelho da fábrica.</summary>
    public static ShopfloorDevice RegisterPos(Guid id, string name, string secretHash, Guid createdBy, Guid storeId, Guid registerId, int epoch, string publicKey, DateTimeOffset now) => new()
    {
        Id = id,
        Kind = DeviceKind.Register,
        Name = name,
        SecretHash = secretHash,
        SecretRotatedAt = now,
        CreatedBy = createdBy,
        CreatedAt = now,
        LastSeenAt = now,
        ExpiresAt = now + Lifetime,
        StoreId = storeId,
        RegisterId = registerId,
        RegisterEpoch = epoch,
        PublicKey = publicKey,
    };

    /// <summary>
    /// Caixa do PDV (ADR 0048 §2, revisão de segurança do PR 227): o segredo anterior deixou de valer. Fica preenchido quando o aparelho usa o segredo
    /// atual (provou que recebeu o novo) ou quando a rotação foi pedida com o anterior (a resposta perdida já foi coberta uma vez). Depois disso,
    /// apresentar o anterior é reuso (dois clones se revezando): o aparelho é revogado. Gravado por UPDATE condicional, fora do diff da auditoria.
    /// </summary>
    [NotAudited]
    public DateTimeOffset? PreviousSecretRetiredAt { get; private set; }

    /// <summary>O segredo anterior ainda vale (nenhum uso do atual nem rotação pedida com o anterior desde a última troca).</summary>
    public bool PreviousSecretStillValid => PreviousSecretHash is not null && PreviousSecretRetiredAt is null;

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
/// <summary>
/// PIN do caixa do PDV (<c>identity.pos_pins</c>, PDV-01 CA 11, ADR 0048 e 0049): um por usuário, vale em todo aparelho de caixa registrado das lojas
/// em que ele pode entrar (o caixa troca de aparelho; o PIN do operador não, e o mesmo PIN vale sem rede, PP-39). Hash forte com o mesmo algoritmo do
/// PIN da fábrica (<c>PasswordHasher</c>, PBKDF2) e as mesmas regras de erro da ADR 0026 §2: 5 seguidos bloqueiam 15 min, o 10º em 24 h apaga.
/// <c>MustChange</c>: o PIN inicial que o gerente deu ao usuário de operação, trocado no primeiro uso.
/// </summary>
public sealed class PosPin : Entity, IAuditable
{
    public Guid UserId { get; init; }

    [ProtectedSecret]
    public string PinHash { get; private set; } = string.Empty;

    public bool MustChange { get; private set; }

    /// <summary>Quem definiu o PIN (o próprio usuário ou o gerente que criou o usuário de operação).</summary>
    public Guid CreatedBy { get; private set; }

    public int FailedCount { get; private set; }

    public DateTimeOffset? LockedUntil { get; private set; }

    public DateTimeOffset? FailuresSince { get; private set; }

    public int FailuresInWindow { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    /// <summary>
    /// OFF-03 (PP-39): código de aprovação sem rede do supervisor, separado do PIN (só o hash PBKDF2). Nunca vale com rede: quebrar o verificador
    /// guardado num aparelho rende, no máximo, aprovações sem rede dentro dos limites da loja.
    /// </summary>
    [ProtectedSecret]
    public string? OfflineCodeHash { get; private set; }

    public DateTimeOffset? OfflineCodeSetAt { get; private set; }

    public void SetOfflineCode(string hash, DateTimeOffset now)
    {
        OfflineCodeHash = hash;
        OfflineCodeSetAt = now;
    }

    public void ClearOfflineCode()
    {
        OfflineCodeHash = null;
        OfflineCodeSetAt = null;
    }

    public static PosPin Create(Guid userId, string pinHash, bool mustChange, Guid createdBy, DateTimeOffset now) => new()
    {
        UserId = userId,
        PinHash = pinHash,
        MustChange = mustChange,
        CreatedBy = createdBy,
        CreatedAt = now,
    };

    /// <summary>PIN novo: zera os erros e o bloqueio.</summary>
    public void Replace(string pinHash, bool mustChange, Guid by, DateTimeOffset now)
    {
        PinHash = pinHash;
        MustChange = mustChange;
        CreatedBy = by;
        CreatedAt = now;
        FailedCount = 0;
        LockedUntil = null;
        FailuresSince = null;
        FailuresInWindow = 0;
    }

    /// <summary>Hash refeito com o mesmo PIN (o <c>PasswordHasher</c> pediu rehash): nada mais muda.</summary>
    public void Rehash(string pinHash) => PinHash = pinHash;

    public bool IsLocked(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>As mesmas regras do <see cref="UserPin"/>: 5 erros seguidos bloqueiam 15 min; o 10º em 24 h apaga o PIN (quem chama remove a linha).</summary>
    public PinFailure RegisterFailure(DateTimeOffset now)
    {
        if (FailuresSince is not { } since || now - since > UserPin.DailyWindow)
        {
            FailuresSince = now;
            FailuresInWindow = 0;
        }

        FailuresInWindow++;
        FailedCount++;
        if (FailuresInWindow >= UserPin.DailyFailureLimit)
        {
            return PinFailure.Erased;
        }

        if (FailedCount < UserPin.MaxConsecutiveFailures)
        {
            return PinFailure.Counted;
        }

        LockedUntil = now + UserPin.LockDuration;
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

/// <summary>Segredo que o verificador sem rede protege (OFF-03): o PIN do operador ou o código de aprovação sem rede do supervisor (PP-39).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PinVerifierKind>))]
public enum PinVerifierKind
{
    OperatorPin,
    SupervisorCode,
}

/// <summary>
/// Verificador sem rede entregue a um aparelho de caixa (<c>identity.device_pin_verifiers</c>, OFF-03 CA 1 e 4): de quem, de que segredo, quando
/// desceu e a impressão digital do hash usado (SHA-256 do hash, nunca o hash). A cada sincronização a lista do aparelho é refeita: quem saiu da loja,
/// foi desativado ou trocou o segredo perde a linha (o aparelho apaga o verificador antigo ao receber a lista nova).
/// </summary>
public sealed class DevicePinVerifier : Entity
{
    public Guid DeviceId { get; init; }

    public Guid UserId { get; init; }

    public PinVerifierKind Kind { get; init; }

    /// <summary>SHA-256 (base64) do hash PBKDF2 do segredo no momento da descida: muda quando o segredo muda.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; set; }
}
