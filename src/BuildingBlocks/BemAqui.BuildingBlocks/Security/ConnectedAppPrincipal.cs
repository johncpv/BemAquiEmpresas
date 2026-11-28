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

/// <summary>
/// Atendente do aplicativo na trilha (INT-04 CA 14): cabeçalho opcional <c>BemAqui-On-Behalf-Of: &lt;id opaco&gt;;&lt;primeiro nome&gt;</c>, até 80 caracteres.
/// Só muda o texto da origem da auditoria ("Integração Conecta (autorizada por Fulano) · atendente Ana"); nunca muda permissão. O nome é sanitizado:
/// só letras, espaço, hífen, apóstrofo e ponto, até 40 caracteres; cabeçalho fora do formato é ignorado.
/// </summary>
public static class OnBehalfOf
{
    public const string Header = "BemAqui-On-Behalf-Of";
    public const int MaxHeaderLength = 80;
    public const int MaxNameLength = 40;
    private const int MaxLabelLength = 300;

    /// <summary>Primeiro nome do atendente, sanitizado; null se o cabeçalho falta ou está fora do formato.</summary>
    public static string? Attendant(string? header)
    {
        if (string.IsNullOrWhiteSpace(header) || header.Length > MaxHeaderLength)
        {
            return null;
        }

        var separator = header.IndexOf(';', StringComparison.Ordinal);
        if (separator <= 0 || separator == header.Length - 1)
        {
            return null;
        }

        var name = new string(header[(separator + 1)..].Where(c => char.IsLetter(c) || c is ' ' or '-' or '\'' or '.').ToArray()).Trim();
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0)
        {
            return null;
        }

        return name.Length > MaxNameLength ? name[..MaxNameLength].TrimEnd() : name;
    }

    /// <summary>Origem da auditoria: "Integração {app} (autorizada por {pessoa})" e, com atendente, " · atendente {nome}" (até 300 caracteres).</summary>
    public static string Label(string appName, string? grantorName, string? header)
    {
        var label = "Integração " + appName + " (autorizada por " + grantorName + ")";
        if (Attendant(header) is { } attendant)
        {
            label += " · atendente " + attendant;
        }

        return label.Length > MaxLabelLength ? label[..MaxLabelLength] : label;
    }
}
