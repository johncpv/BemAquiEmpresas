using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>Visões do Painel da TV (ADR 0030 §5): quadro por etapa, fila e previsões, atrasos. O conteúdo de cada uma é da PNL-01b.</summary>
public static class KioskViews
{
    public const string Board = "board";
    public const string Queue = "queue";
    public const string Late = "late";

    public static readonly string[] All = [Board, Queue, Late];
}

/// <summary>Situação da tela na lista (PNL-01a CA 2): aguardando a primeira conexão, ativa (pareada) ou revogada.</summary>
public enum KioskScreenStatus
{
    AwaitingConnection,
    Active,
    Revoked,
}

/// <summary>
/// Tela de TV (<c>identity.kiosk_tokens</c>, ADR 0030 §1 e §4): token de 256 bits guardado só como SHA-256, com os 6 primeiros
/// caracteres para identificar; pareamento de uso único (<see cref="ConsumedAt"/>): o token vira uma sessão e não abre outra.
/// Escopo (linha, visões e rotação) aplicado no servidor. Auditada sem o token nem o hash.
/// </summary>
[DocumentName("A tela de TV")]
public sealed class KioskScreen : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxNameLength = 60;
    public const int MaxLineLength = 60;
    public const int MaxNetworks = 5;
    public const int MinRotationSeconds = 10;
    public const int MaxRotationSeconds = 120;
    public const int DefaultRotationSeconds = 20;
    public const int PrefixLength = 6;

    public string Name { get; private set; } = string.Empty;

    [ProtectedSecret]
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>Seis primeiros caracteres do token, para reconhecer o link (nunca o token inteiro).</summary>
    public string TokenPrefix { get; private set; } = string.Empty;

    /// <summary>Linha dos centros de trabalho; null = todas.</summary>
    public string? Line { get; private set; }

    public string[] Views { get; private set; } = [.. KioskViews.All];

    public int RotationSeconds { get; private set; } = DefaultRotationSeconds;

    /// <summary>Redes permitidas (CIDR, até 5); vazia = qualquer rede.</summary>
    public string[] AllowedNetworks { get; private set; } = [];

    /// <summary>Quando o token atual virou sessão: depois disso ele não abre outra (pareamento de uso único).</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    public Guid CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastSeenAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public uint Version { get; set; }

    public static KioskScreen Create(string name, string? line, IEnumerable<string> views, int rotationSeconds, IEnumerable<string> networks, string tokenHash, string tokenPrefix, Guid createdBy, DateTimeOffset now)
    {
        var screen = new KioskScreen { CreatedBy = createdBy, CreatedAt = now };
        screen.Configure(name, line, views, rotationSeconds, networks);
        screen.TokenHash = tokenHash;
        screen.TokenPrefix = tokenPrefix;
        return screen;
    }

    public void Configure(string name, string? line, IEnumerable<string> views, int rotationSeconds, IEnumerable<string> networks)
    {
        Name = name;
        Line = line;
        Views = [.. views];
        RotationSeconds = rotationSeconds;
        AllowedNetworks = [.. networks];
    }

    public bool IsRevoked => RevokedAt is not null;

    public KioskScreenStatus Status => RevokedAt is not null ? KioskScreenStatus.Revoked : ConsumedAt is null ? KioskScreenStatus.AwaitingConnection : KioskScreenStatus.Active;

    /// <summary>"Gerar novo link": token novo, ainda não usado; as sessões anteriores caem (quem chama revoga).</summary>
    public void ReplaceToken(string tokenHash, string tokenPrefix)
    {
        TokenHash = tokenHash;
        TokenPrefix = tokenPrefix;
        ConsumedAt = null;
    }

    public void Consume(DateTimeOffset now)
    {
        ConsumedAt = now;
        LastSeenAt = now;
    }

    public void Revoke(Guid? by, DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedBy = by;
    }
}

/// <summary>
/// Sessão de quiosque (<c>identity.kiosk_sessions</c>, ADR 0030 §2): o cookie <c>__Host-bq_kiosk</c> é opaco (32 bytes) e só o hash
/// fica aqui. Vence com 30 dias sem uso ou 180 dias desde o pareamento, o que vier primeiro. No máximo uma ativa por tela.
/// </summary>
public sealed class KioskSession : Entity
{
    public static readonly TimeSpan UnusedLimit = TimeSpan.FromDays(30);
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(180);

    /// <summary><c>last_seen_at</c> gravado no máximo 1 vez por minuto (CA 19).</summary>
    public static readonly TimeSpan SeenGranularity = TimeSpan.FromMinutes(1);

    [ProtectedSecret]
    public string TokenHash { get; init; } = string.Empty;

    public Guid KioskId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Vencimento por falta de uso: última vez vista + 30 dias, sem passar do absoluto.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset AbsoluteExpiresAt { get; init; }

    public string? Ip { get; init; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static KioskSession Open(Guid kioskId, string tokenHash, string? ip, DateTimeOffset now) => new()
    {
        KioskId = kioskId,
        TokenHash = tokenHash,
        Ip = ip,
        CreatedAt = now,
        LastSeenAt = now,
        ExpiresAt = Min(now + UnusedLimit, now + Lifetime),
        AbsoluteExpiresAt = now + Lifetime,
    };

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now && AbsoluteExpiresAt > now;

    /// <summary>Uso da TV: renova o prazo de 30 dias (sem passar dos 180). Devolve true quando gravou (no máximo 1 vez por minuto).</summary>
    public bool Touch(DateTimeOffset now)
    {
        if (now - LastSeenAt < SeenGranularity)
        {
            return false;
        }

        LastSeenAt = now;
        ExpiresAt = Min(now + UnusedLimit, AbsoluteExpiresAt);
        return true;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
