using System.Security.Claims;

namespace BemAqui.BuildingBlocks.Security;

/// <summary>
/// Aplicativo conectado (INT-01, ADR 0053 §3 e §4): esquema <c>App</c>, aceito só em <c>/api/v1/integration/*</c>. O principal nunca tem
/// <c>sub</c> de pessoa: tem o aplicativo, a conexão, quem autorizou e o tenant. Os tokens são opacos (nunca JWT), com prefixo próprio.
/// </summary>
public static class AppClaims
{
    public const string Scheme = "App";
    public const string Policy = "connected-app";
    public const string App = "app";
    public const string Connection = "conn";
    public const string Grantor = "grantor";
    public const string Tenant = "tid";

    /// <summary>Prefixo do token de acesso (15 min).</summary>
    public const string AccessPrefix = "bqa_";

    /// <summary>Prefixo do token de renovação (rotativo, 90 dias sem uso).</summary>
    public const string RefreshPrefix = "bqr_";

    /// <summary>Token opaco de aplicativo (de acesso ou de renovação): nunca é lido como JWT.</summary>
    public static bool IsAppToken(string? token) =>
        token is not null && (token.StartsWith(AccessPrefix, StringComparison.Ordinal) || token.StartsWith(RefreshPrefix, StringComparison.Ordinal));

    public static bool IsApp(ClaimsPrincipal? user) =>
        user?.Identity?.AuthenticationType == Scheme && user.FindFirst(Connection) is not null && user.FindFirst("sub") is null;

    public static Guid? ConnectionOf(ClaimsPrincipal? user) =>
        IsApp(user) && Guid.TryParse(user!.FindFirst(Connection)?.Value, out var id) ? id : null;

    public static Guid? GrantorOf(ClaimsPrincipal? user) =>
        IsApp(user) && Guid.TryParse(user!.FindFirst(Grantor)?.Value, out var id) ? id : null;

    public static ClaimsPrincipal Create(Guid tenantId, ConnectedAppGrant grant)
    {
        ArgumentNullException.ThrowIfNull(grant);
        var identity = new ClaimsIdentity(
        [
            new Claim(Tenant, tenantId.ToString()),
            new Claim(App, grant.ClientId),
            new Claim(Connection, grant.ConnectionId.ToString()),
            new Claim(Grantor, grant.GrantorId.ToString()),
        ], Scheme);
        return new ClaimsPrincipal(identity);
    }
}

/// <summary>Concessão da conexão em uso no request (o que o esquema <c>App</c> achou pelo hash do token).</summary>
public sealed record ConnectedAppGrant(Guid ConnectionId, Guid AppId, string ClientId, string AppName, Guid GrantorId, string GrantorName, IReadOnlySet<string> Scopes);

/// <summary>
/// Conexão do request (escopo do request): o esquema <c>App</c> preenche; a permissão efetiva do autorizador passa a ser limitada à concessão
/// em todo o processamento (ADR 0053 §6), inclusive nas checagens feitas dentro dos serviços.
/// </summary>
public sealed class ConnectedAppContext
{
    public ConnectedAppGrant? Current { get; set; }
}
