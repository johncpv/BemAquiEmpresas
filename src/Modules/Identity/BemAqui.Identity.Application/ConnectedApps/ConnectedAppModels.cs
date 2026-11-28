using System.Text.Json.Serialization;

namespace BemAqui.Identity.Application.ConnectedApps;

/// <summary>Início da conexão no ERP (CA 1): o front navega para o Conecta com o host do tenant e a intenção.</summary>
public sealed record ConnectStart(string RedirectUrl);

/// <summary>"Conectar" (INT-04 CA 9): o código de pareamento gerado no Conecta (<c>XXXX-XXXX</c>); opcional só em "Autorizar de novo".</summary>
public sealed record ConnectStartInput(string? PairingCode);

/// <summary>Pedido do <c>/oauth/authorize</c>, como chegou na URL; o front repassa sem alterar (CA 3).</summary>
public sealed record AuthorizeRequest(
    string? ResponseType,
    string? ClientId,
    string? RedirectUri,
    string? Scope,
    string? State,
    string? CodeChallenge,
    string? CodeChallengeMethod,
    string? Intent,
    string? AccountLabel);

/// <summary>Consentimento: o pedido e o código do aplicativo autenticador (2FA de novo, CA 7).</summary>
public sealed record AuthorizeConsentInput(AuthorizeRequest Request, string? Code);

public sealed record AppScopeView(string Scope, string Text);

/// <summary>
/// Resultado da conferência (CA 3 a 6): <c>consent</c> mostra a tela; <c>error</c> mostra o erro no ERP, sem redirecionar; <c>redirect</c> volta ao
/// aplicativo com o erro OAuth e o <c>state</c>.
/// </summary>
public sealed record AuthorizeInspection(
    string Outcome,
    string? ErrorCode,
    string? Message,
    string? RedirectTo,
    string? AppName,
    string? AccountLabel,
    string? TenantName,
    IReadOnlyList<AppScopeView> Granted,
    IReadOnlyList<AppScopeView> NotHeld)
{
    public const string Consent = "consent";
    public const string Error = "error";
    public const string Redirect = "redirect";
}

public sealed record AuthorizeRedirect(string RedirectTo);

/// <summary>Resposta do <c>/oauth/token</c> (RFC 6749 §5.1), com a conexão e o segredo dos avisos (INT-02, uma vez só).</summary>
public sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("scope")] string Scope,
    [property: JsonPropertyName("connection_id")] Guid ConnectionId,
    [property: JsonPropertyName("webhook_secret")] string? WebhookSecret);

/// <summary>Erro do OAuth (RFC 6749 §5.2): corpo uniforme.</summary>
public sealed record OAuthError(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string Description)
{
    public bool IsClientError => Error == InvalidClient;

    public const string InvalidClient = "invalid_client";
    public const string InvalidGrant = "invalid_grant";
    public const string InvalidRequest = "invalid_request";
    public const string UnsupportedGrantType = "unsupported_grant_type";
}

/// <summary>Situação da conexão na tela (CA 20): chip, motivo em linguagem simples e a ação.</summary>
public sealed record AppConnectionView(
    Guid Id,
    string Status,
    string Reason,
    string StatusText,
    string ReasonText,
    string? AccountLabel,
    Guid GrantorUserId,
    string? GrantorName,
    DateTimeOffset ConnectedAt,
    DateTimeOffset ReconsentDueAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    IReadOnlyList<AppScopeView> Scopes,
    IReadOnlyList<AppScopeView>? CanGain = null);

/// <summary>Página Configurações → Integrações → Conecta: o aplicativo, o que ele pode pedir e a última conexão (ativa, suspensa ou a revogada mais recente).</summary>
public sealed record ConnectedAppPage(string ClientId, string Name, bool Configured, IReadOnlyList<AppScopeView> AllowedScopes, AppConnectionView? Connection);

/// <summary>Linha de "Aplicativos conectados" (CA 20).</summary>
public sealed record ConnectedAppListItem(string ClientId, string Name, AppConnectionView? Connection);

/// <summary>A própria conexão vista pelo aplicativo (<c>GET /api/v1/integration/connection</c>).</summary>
public sealed record IntegrationConnectionView(
    [property: JsonPropertyName("connection_id")] Guid ConnectionId,
    [property: JsonPropertyName("app")] string App,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string> Scopes,
    [property: JsonPropertyName("account_label")] string? AccountLabel,
    [property: JsonPropertyName("connected_at")] DateTimeOffset ConnectedAt,
    [property: JsonPropertyName("reconsent_due_at")] DateTimeOffset ReconsentDueAt);
