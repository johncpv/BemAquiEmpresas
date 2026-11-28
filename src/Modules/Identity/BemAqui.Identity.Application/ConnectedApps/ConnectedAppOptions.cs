using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Modules;

namespace BemAqui.Identity.Application.ConnectedApps;

/// <summary>
/// Conecta como aplicativo primeira parte (INT-01, ADR 0053 §2): cliente confidencial com um segredo por ambiente (igual em todos os tenants),
/// guardado como arquivo de secret; aqui fica só o SHA-256 em hexadecimal (<c>Integrations__Conecta__ClientSecretHash</c>). Sem o hash, nenhum
/// cliente se autentica (falha fechada).
/// </summary>
public sealed class ConnectedAppOptions
{
    public const string Section = "Integrations:Conecta";

    public string ClientId { get; set; } = "conecta";

    public string Name { get; set; } = "Conecta";

    /// <summary>Página do Conecta que pede login ou a escolha da conta e volta ao <c>/oauth/authorize</c> do tenant.</summary>
    public string StartUrl { get; set; } = string.Empty;

    /// <summary>URIs de retorno exatas, só <c>https</c>; a comparação é caractere a caractere.</summary>
    public string[] RedirectUris { get; set; } = [];

    /// <summary>Vazio = a lista da ADR 0053 (<see cref="ConnectedAppScopes.Conecta"/>).</summary>
    public string[] AllowedScopes { get; set; } = [];

    public string RequiredModule { get; set; } = "conecta";

    public string ClientSecretHash { get; set; } = string.Empty;

    public IReadOnlyList<string> EffectiveScopes => AllowedScopes.Length == 0 ? ConnectedAppScopes.Conecta : AllowedScopes;
}

/// <summary>Regras puras do OAuth (INT-01 CA 3, 5, 7 e 11), testadas com os vetores das RFCs.</summary>
public static partial class OAuthRules
{
    /// <summary>Letras e números do código de pareamento, sem os ambíguos (0, O, 1, I e L), como o Conecta gera (INT-04 CA 9).</summary>
    public const string PairingAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    /// <summary>
    /// Código de pareamento do Conecta (INT-04 CA 9) no formato <c>XXXX-XXXX</c>: aceita minúsculas, espaços nas pontas e sem o hífen; devolve o código
    /// normalizado ou null fora do formato. O ERP só confere o formato: quem valida o código (expiração, tentativas) é o Conecta.
    /// </summary>
    public static string? NormalizePairing(string? code)
    {
        var text = (code ?? string.Empty).Trim().ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal);
        if (text.Length != 8 || text.Any(c => !PairingAlphabet.Contains(c, StringComparison.Ordinal)))
        {
            return null;
        }

        return text[..4] + "-" + text[4..];
    }

    /// <summary>RFC 7636 §4.2: 43 caracteres base64url (SHA-256 sem preenchimento).</summary>
    public static bool IsValidChallenge(string? challenge) => challenge is { Length: 43 } && Base64Url().IsMatch(challenge);

    /// <summary>RFC 7636 §4.1: 43 a 128 caracteres não reservados.</summary>
    public static bool IsValidVerifier(string? verifier) => verifier is { Length: >= 43 and <= 128 } && Unreserved().IsMatch(verifier);

    public static string Challenge(string verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        return Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    /// <summary>S256 em tempo constante.</summary>
    public static bool VerifyPkce(string? verifier, string challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        if (!IsValidVerifier(verifier))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Challenge(verifier!)), Encoding.ASCII.GetBytes(challenge));
    }

    /// <summary>A <c>redirect_uri</c> é uma das cadastradas, caractere a caractere (sem normalizar caixa, barra final ou query).</summary>
    public static bool RedirectMatches(string? uri, IEnumerable<string> registered) =>
        uri is not null && registered.Any(r => string.Equals(r, uri, StringComparison.Ordinal));

    /// <summary>Hash que amarra a intenção ao pedido (CA 5): <c>state</c>, <c>code_challenge</c> e <c>redirect_uri</c> com o tamanho de cada um.</summary>
    public static string BindingHash(string? state, string? challenge, string? redirectUri)
    {
        var text = string.Join('|', new[] { state ?? string.Empty, challenge ?? string.Empty, redirectUri ?? string.Empty }
            .Select(p => p.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + p));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>Segredo do cliente contra o SHA-256 configurado, em tempo constante. Sem hash configurado, recusa.</summary>
    public static bool SecretMatches(string? secret, string? configuredHash)
    {
        if (string.IsNullOrEmpty(secret) || string.IsNullOrWhiteSpace(configuredHash) || configuredHash.Trim().Length != 64)
        {
            return false;
        }

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(configuredHash.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(secret)), expected);
    }

    /// <summary><c>client_secret_basic</c> (RFC 6749 §2.3.1): id e segredo codificados como formulário antes do base64.</summary>
    public static (string ClientId, string Secret)? ParseBasic(string? authorization)
    {
        const string prefix = "Basic ";
        if (authorization is null || !authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(authorization[prefix.Length..].Trim()));
            var separator = raw.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                return null;
            }

            return (Uri.UnescapeDataString(raw[..separator].Replace('+', ' ')), Uri.UnescapeDataString(raw[(separator + 1)..].Replace('+', ' ')));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Junta parâmetros à URI de retorno, preservando a query cadastrada.</summary>
    public static string AppendQuery(string uri, IEnumerable<KeyValuePair<string, string?>> values)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var query = string.Join('&', values.Where(v => v.Value is not null).Select(v => Uri.EscapeDataString(v.Key) + "=" + Uri.EscapeDataString(v.Value!)));
        return uri + (uri.Contains('?', StringComparison.Ordinal) ? "&" : "?") + query;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex Base64Url();

    [GeneratedRegex("^[A-Za-z0-9._~-]+$")]
    private static partial Regex Unreserved();
}
