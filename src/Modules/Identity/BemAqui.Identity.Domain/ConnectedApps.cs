using BemAqui.SharedKernel;

namespace BemAqui.Identity.Domain;

/// <summary>
/// Aplicativo conectado conhecido pelo tenant (<c>identity.connected_apps</c>, INT-01, ADR 0053 §2). O Conecta é primeira parte: a linha nasce pelo
/// seeder em todo tenant; nome, URIs, escopos permitidos e o hash do segredo vêm da configuração do ambiente (<c>Integrations:Conecta</c>).
/// </summary>
public sealed class ConnectedApp : Entity
{
    public const int MaxClientIdLength = 40;
    public const int MaxNameLength = 80;

    public required string ClientId { get; init; }

    public string Name { get; set; } = string.Empty;

    public string RequiredModule { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Intenção de conexão (<c>identity.app_connect_intents</c>, INT-01 CA 1, 4 e 5): criada por quem clica "Conectar" no ERP, vale 10 min e uma vez.
/// Só o hash do valor opaco fica no banco. No primeiro <c>inspect</c> válido guarda o hash de <c>state + code_challenge + redirect_uri</c>, e o
/// consentimento só passa com o mesmo pedido.
/// </summary>
public sealed class AppConnectIntent : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public required string IntentHash { get; init; }

    public Guid UserId { get; init; }

    public Guid AppId { get; init; }

    public string? RequestBindingHash { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? UsedAt { get; private set; }

    public bool IsUsable(Guid userId, Guid appId, DateTimeOffset now) => UsedAt is null && ExpiresAt > now && UserId == userId && AppId == appId;

    /// <summary>Amarra a intenção ao primeiro pedido válido; depois, só o mesmo pedido confere.</summary>
    public bool Bind(string bindingHash)
    {
        RequestBindingHash ??= bindingHash;
        return string.Equals(RequestBindingHash, bindingHash, StringComparison.Ordinal);
    }

    public void MarkUsed(DateTimeOffset now) => UsedAt ??= now;
}

/// <summary>
/// Código de autorização (<c>identity.app_authorization_codes</c>, INT-01 CA 7 e 11): 32 bytes, 60 s, uso único, só o hash; ligado ao aplicativo, à
/// <c>redirect_uri</c>, ao <c>code_challenge</c> (S256), aos escopos concedidos e a quem autorizou. Reapresentado depois de usado, revoga a conexão
/// que ele criou.
/// </summary>
public sealed class AppAuthorizationCode : Entity
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public required string CodeHash { get; init; }

    public Guid AppId { get; init; }

    public Guid UserId { get; init; }

    public required string RedirectUri { get; init; }

    public required string CodeChallenge { get; init; }

    public string[] Scopes { get; init; } = [];

    public string? AccountLabel { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? UsedAt { get; private set; }

    public Guid? ConnectionId { get; private set; }

    public void Redeem(Guid connectionId, DateTimeOffset now)
    {
        UsedAt ??= now;
        ConnectionId ??= connectionId;
    }
}

public enum AppConnectionStatus
{
    Active,
    Suspended,
    Revoked,
}

/// <summary>Motivo da situação da conexão: a tela traduz em linguagem simples (INT-01 CA 20).</summary>
public enum AppConnectionReason
{
    None,

    /// <summary>Quem autorizou foi desativado ou desligado (suspensa).</summary>
    GrantorInactive,

    /// <summary>12 meses desde o consentimento (suspensa).</summary>
    ReconsentDue,

    /// <summary>"Desconectar" no ERP.</summary>
    DisconnectedByUser,

    /// <summary>Revogação pelo aplicativo (RFC 7009).</summary>
    DisconnectedByApp,

    /// <summary>Token de renovação ou código reapresentado: desconectada por segurança.</summary>
    CredentialReuse,

    /// <summary>Substituída por um novo consentimento.</summary>
    Replaced,

    /// <summary>Módulo <c>conecta</c> desligado há mais de 30 dias.</summary>
    ModuleOff,
}

/// <summary>
/// Conexão de um aplicativo com o tenant (<c>identity.app_connections</c>, INT-01): no máximo uma não revogada por aplicativo. Ciclo
/// <c>Active</c> → <c>Suspended</c> (autorizador inativo ou 12 meses) → só sai por novo consentimento (conexão nova) ou <c>Revoked</c>, que é final.
/// </summary>
[DocumentName("A conexão do aplicativo")]
public sealed class AppConnection : Entity, IAuditable, IConcurrencyVersioned
{
    public const int MaxAccountLabelLength = 120;
    public const int ReconsentMonths = 12;
    public static readonly TimeSpan ModuleOffGrace = TimeSpan.FromDays(30);
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    public Guid AppId { get; init; }

    public Guid GrantorUserId { get; init; }

    public string[] GrantedScopes { get; init; } = [];

    public string? AccountLabel { get; init; }

    public AppConnectionStatus Status { get; private set; } = AppConnectionStatus.Active;

    public AppConnectionReason StatusReason { get; private set; } = AppConnectionReason.None;

    public DateTimeOffset ConnectedAt { get; init; }

    public DateTimeOffset ReconsentDueAt { get; init; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? RevokedBy { get; private set; }

    public string? RevokedReason { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    [PersonalData]
    public string? LastUsedIp { get; private set; }

    /// <summary>Desde quando o módulo <c>conecta</c> está desligado (a revogação vem depois de 30 dias).</summary>
    public DateTimeOffset? ModuleOffSince { get; private set; }

    public uint Version { get; set; }

    public static AppConnection Create(Guid appId, Guid grantorUserId, IEnumerable<string> scopes, string? accountLabel, DateTimeOffset now) => new()
    {
        AppId = appId,
        GrantorUserId = grantorUserId,
        GrantedScopes = [.. scopes],
        AccountLabel = accountLabel,
        ConnectedAt = now,
        ReconsentDueAt = now.AddMonths(ReconsentMonths),
    };

    public bool Suspend(AppConnectionReason reason, DateTimeOffset now)
    {
        if (Status != AppConnectionStatus.Active)
        {
            return false;
        }

        Status = AppConnectionStatus.Suspended;
        StatusReason = reason;
        SuspendedAt = now;
        return true;
    }

    public bool Revoke(AppConnectionReason reason, string text, Guid? by, DateTimeOffset now)
    {
        if (Status == AppConnectionStatus.Revoked)
        {
            return false;
        }

        Status = AppConnectionStatus.Revoked;
        StatusReason = reason;
        RevokedAt = now;
        RevokedBy = by;
        RevokedReason = text;
        return true;
    }

    /// <summary>Último uso e IP, no máximo uma gravação por minuto (CA 18).</summary>
    public bool Touch(string? ip, DateTimeOffset now)
    {
        if (LastUsedAt is { } last && now - last < TouchInterval && string.Equals(LastUsedIp, ip, StringComparison.Ordinal))
        {
            return false;
        }

        LastUsedAt = now;
        LastUsedIp = ip;
        return true;
    }

    public void MarkModuleOff(bool off, DateTimeOffset now) => ModuleOffSince = off ? ModuleOffSince ?? now : null;
}

public enum AppTokenKind
{
    Access,
    Refresh,
}

/// <summary>
/// Token opaco de aplicativo (<c>identity.app_tokens</c>, ADR 0053 §3): só o SHA-256. Acesso <c>bqa_</c> de 15 min; renovação <c>bqr_</c> rotativa
/// na mesma família, vencendo com 90 dias sem uso. Reapresentar um substituído fora da tolerância revoga a conexão.
/// </summary>
public sealed class AppToken : Entity
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan RefreshIdleLifetime = TimeSpan.FromDays(90);
    public static readonly TimeSpan ReuseTolerance = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PurgeAfter = TimeSpan.FromDays(30);

    public required string TokenHash { get; init; }

    public AppTokenKind Kind { get; init; }

    public Guid ConnectionId { get; init; }

    public Guid FamilyId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset? ReplacedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ReplacedAt is null && ExpiresAt > now;

    public void ReplaceWith(Guid next, DateTimeOffset now)
    {
        ReplacedAt ??= now;
        ReplacedById ??= next;
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
