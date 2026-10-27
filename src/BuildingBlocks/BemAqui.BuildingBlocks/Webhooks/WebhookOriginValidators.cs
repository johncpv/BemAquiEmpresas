using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>
/// O que chegou, como chegou (FND-16 CA 5 e 6): os bytes crus do corpo (antes de qualquer desserialização), os cabeçalhos
/// (sem diferença de maiúsculas) e o token da query (<c>?t=</c>) ou do último segmento do caminho.
/// </summary>
public sealed class WebhookRequest(ReadOnlyMemory<byte> body, IReadOnlyDictionary<string, string> headers, string? queryToken, string? pathToken)
{
    public ReadOnlyMemory<byte> Body { get; } = body;

    public IReadOnlyDictionary<string, string> Headers { get; } = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);

    public string? QueryToken { get; } = queryToken;

    public string? PathToken { get; } = pathToken;

    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>Como a origem é provada: segredo de assinatura (cifrado, <c>webhook-secret</c>) ou token na URL (só o hash guardado).</summary>
public enum WebhookCredentialKind
{
    HmacSecret,
    UrlToken,
}

/// <summary>Resultado da validação: recusado, pela credencial atual ou pela anterior (sobreposição de 24 h, CA 7).</summary>
public enum WebhookOriginMatch
{
    Invalid,
    Current,
    Previous,
}

/// <summary>
/// Chaves de uma credencial, já prontas para comparar: o segredo decifrado (HMAC) ou o SHA-256 do token. A anterior só vale
/// até <see cref="PreviousValidUntil"/>. Quem cria zera os bytes depois do uso.
/// </summary>
public sealed record WebhookKeys(byte[]? Current, byte[]? Previous, DateTimeOffset? PreviousValidUntil)
{
    public bool PreviousActive(DateTimeOffset now) => Previous is not null && PreviousValidUntil is { } until && until > now;

    public void Clear()
    {
        if (Current is not null)
        {
            CryptographicOperations.ZeroMemory(Current);
        }

        if (Previous is not null)
        {
            CryptographicOperations.ZeroMemory(Previous);
        }
    }
}

/// <summary>Validador de origem de um provedor (ADR 0031 §2): sempre em tempo constante, sobre o corpo cru.</summary>
public interface IWebhookOriginValidator
{
    WebhookCredentialKind Kind { get; }

    WebhookOriginMatch Validate(WebhookRequest request, WebhookKeys keys, DateTimeOffset now);
}

/// <summary>
/// HMAC-SHA256 do corpo cru (CA 5): cabeçalho e prefixo por provedor (padrão do EmissorFiscal: <c>X-BemAqui-Signature: sha256=…</c>).
/// Com <paramref name="timestampHeader"/>, a assinatura cobre <c>{timestamp}.{corpo}</c> e o timestamp fora da janela é recusado
/// (anti-replay, ADR 0031 §2). As duas credenciais são sempre calculadas, para o tempo não dizer qual valeu.
/// </summary>
public sealed class HmacSha256Validator(string signatureHeader = "X-BemAqui-Signature", string prefix = "sha256=", string? timestampHeader = null, TimeSpan? window = null)
    : IWebhookOriginValidator
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);

    public string SignatureHeader { get; } = signatureHeader;

    public string Prefix { get; } = prefix;

    public string? TimestampHeader { get; } = timestampHeader;

    public TimeSpan Window { get; } = window ?? DefaultWindow;

    public WebhookCredentialKind Kind => WebhookCredentialKind.HmacSecret;

    public WebhookOriginMatch Validate(WebhookRequest request, WebhookKeys keys, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Current is null || !TryReadSignature(request.Header(SignatureHeader), out var signature))
        {
            return WebhookOriginMatch.Invalid;
        }

        byte[]? prefixBytes = null;
        if (TimestampHeader is not null)
        {
            var timestamp = request.Header(TimestampHeader);
            if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || seconds > 253402300799 // 9999-12-31
                || (now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > Window)
            {
                return WebhookOriginMatch.Invalid;
            }

            prefixBytes = Encoding.ASCII.GetBytes(timestamp + ".");
        }

        var current = Matches(keys.Current, prefixBytes, request.Body.Span, signature);
        var previous = keys.Previous is not null && Matches(keys.Previous, prefixBytes, request.Body.Span, signature);
        if (current)
        {
            return WebhookOriginMatch.Current;
        }

        return previous && keys.PreviousActive(now) ? WebhookOriginMatch.Previous : WebhookOriginMatch.Invalid;
    }

    /// <summary>Assinatura do corpo (para os testes e para o provedor de teste).</summary>
    public static string Sign(byte[] key, ReadOnlySpan<byte> body, string? timestamp = null, string prefix = "sha256=")
    {
        ArgumentNullException.ThrowIfNull(key);
        var content = timestamp is null ? body.ToArray() : [.. Encoding.ASCII.GetBytes(timestamp + "."), .. body];
        return prefix + Convert.ToHexStringLower(HMACSHA256.HashData(key, content));
    }

    private bool TryReadSignature(string? header, out byte[] signature)
    {
        signature = [];
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var hex = header.AsSpan(Prefix.Length).Trim();
        if (hex.Length != 64)
        {
            return false;
        }

        try
        {
            signature = Convert.FromHexString(hex);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool Matches(byte[] key, byte[]? prefix, ReadOnlySpan<byte> body, byte[] signature)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        if (prefix is not null)
        {
            hmac.AppendData(prefix);
        }

        hmac.AppendData(body);
        Span<byte> expected = stackalloc byte[32];
        hmac.GetHashAndReset(expected);
        return CryptographicOperations.FixedTimeEquals(expected, signature);
    }
}

/// <summary>
/// HMAC-SHA256 no formato do EmissorFiscal (FIS-01 CA 11, lacuna L-g da ADR 0027; o mesmo do Stripe):
/// <c>X-BemAqui-Signature: t={timestamp},v1={hex}[,v1={hex}]</c>, assinatura sobre <c>{timestamp}.{corpo cru}</c>. Na troca de segredo o
/// Emissor manda dois <c>v1</c> por 24 h; basta um conferir. O <c>t</c> fora da janela (5 min) é recusado antes de qualquer HMAC, e o
/// <c>X-BemAqui-Timestamp</c>, quando vem, tem de ser igual ao <c>t</c>. Toda comparação é em tempo constante e as duas credenciais
/// são sempre calculadas.
/// </summary>
public sealed class TimestampedSignatureValidator(string signatureHeader = "X-BemAqui-Signature", string? timestampHeader = "X-BemAqui-Timestamp", TimeSpan? window = null)
    : IWebhookOriginValidator
{
    /// <summary>Mais do que isso no cabeçalho é recusado (o Emissor manda no máximo dois, na rotação).</summary>
    public const int MaxSignatures = 4;

    public string SignatureHeader { get; } = signatureHeader;

    public string? TimestampHeader { get; } = timestampHeader;

    public TimeSpan Window { get; } = window ?? HmacSha256Validator.DefaultWindow;

    public WebhookCredentialKind Kind => WebhookCredentialKind.HmacSecret;

    public WebhookOriginMatch Validate(WebhookRequest request, WebhookKeys keys, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Current is null || !TryParse(request.Header(SignatureHeader), out var timestamp, out var signatures))
        {
            return WebhookOriginMatch.Invalid;
        }

        if (TimestampHeader is not null && request.Header(TimestampHeader) is { } separate && !string.Equals(separate.Trim(), timestamp, StringComparison.Ordinal))
        {
            return WebhookOriginMatch.Invalid;
        }

        if (!long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds > 253402300799 // 9999-12-31
            || (now - DateTimeOffset.FromUnixTimeSeconds(seconds)).Duration() > Window)
        {
            return WebhookOriginMatch.Invalid;
        }

        var prefix = Encoding.ASCII.GetBytes(timestamp + ".");
        var current = Matches(keys.Current, prefix, request.Body.Span, signatures);
        var previous = keys.Previous is not null && Matches(keys.Previous, prefix, request.Body.Span, signatures);
        if (current)
        {
            return WebhookOriginMatch.Current;
        }

        return previous && keys.PreviousActive(now) ? WebhookOriginMatch.Previous : WebhookOriginMatch.Invalid;
    }

    /// <summary>Cabeçalho assinado como o Emissor manda (para os testes e o fake HTTP): um <c>v1</c> por chave, na ordem dada.</summary>
    public static string Sign(string timestamp, ReadOnlySpan<byte> body, params byte[][] keys)
    {
        ArgumentException.ThrowIfNullOrEmpty(timestamp);
        ArgumentNullException.ThrowIfNull(keys);
        var content = (byte[])[.. Encoding.ASCII.GetBytes(timestamp + "."), .. body];
        return "t=" + timestamp + string.Concat(keys.Select(k => ",v1=" + Convert.ToHexStringLower(HMACSHA256.HashData(k, content))));
    }

    private static bool TryParse(string? header, out string timestamp, out List<byte[]> signatures)
    {
        timestamp = string.Empty;
        signatures = [];
        if (string.IsNullOrWhiteSpace(header) || header.Length > 600)
        {
            return false;
        }

        string? t = null;
        foreach (var part in header.Split(',', StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                return false;
            }

            var key = part[..eq];
            var value = part[(eq + 1)..];
            if (key == "t")
            {
                if (t is not null || value.Length is 0 or > 12)
                {
                    return false; // dois t: ambíguo, recusa
                }

                t = value;
            }
            else if (key == "v1")
            {
                if (value.Length != 64 || signatures.Count >= MaxSignatures)
                {
                    return false;
                }

                try
                {
                    signatures.Add(Convert.FromHexString(value));
                }
                catch (FormatException)
                {
                    return false;
                }
            }

            // Outras versões (v0, v2) são ignoradas, como no Stripe.
        }

        if (t is null || signatures.Count == 0)
        {
            return false;
        }

        timestamp = t;
        return true;
    }

    private static bool Matches(byte[] key, byte[] prefix, ReadOnlySpan<byte> body, List<byte[]> signatures)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hmac.AppendData(prefix);
        hmac.AppendData(body);
        Span<byte> expected = stackalloc byte[32];
        hmac.GetHashAndReset(expected);
        var any = false;
        foreach (var signature in signatures)
        {
            any |= CryptographicOperations.FixedTimeEquals(expected, signature);
        }

        return any;
    }
}

/// <summary>Onde o token chega (CA 6): na query <c>?t=</c> (padrão; o log do nginx grava só o caminho) ou no último segmento do caminho.</summary>
public enum UrlTokenMode
{
    Query,
    PathToken,
}

/// <summary>
/// Token de 256 bits na URL para o provedor que não assina o corpo (CA 6, o Inter, ADR 0032): só o SHA-256 fica guardado e a
/// comparação é em tempo constante. O token no lugar errado (caminho no modo query, ou o contrário) é recusado.
/// </summary>
public sealed class UrlTokenValidator(UrlTokenMode mode = UrlTokenMode.Query) : IWebhookOriginValidator
{
    public const string QueryParameter = "t";

    /// <summary>32 bytes em base64url sem preenchimento.</summary>
    public const int TokenLength = 43;

    public UrlTokenMode Mode { get; } = mode;

    public WebhookCredentialKind Kind => WebhookCredentialKind.UrlToken;

    public WebhookOriginMatch Validate(WebhookRequest request, WebhookKeys keys, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(keys);
        var (token, misplaced) = Mode == UrlTokenMode.Query ? (request.QueryToken, request.PathToken) : (request.PathToken, request.QueryToken);
        return misplaced is null ? Compare(token, keys, now) : WebhookOriginMatch.Invalid;
    }

    /// <summary>Compara um token com o hash atual e o anterior (também usado no GET de verificação, CA 1).</summary>
    public static WebhookOriginMatch Compare(string? token, WebhookKeys keys, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Current is null || token is null || token.Length != TokenLength)
        {
            return WebhookOriginMatch.Invalid;
        }

        var hash = Hash(token);
        var current = CryptographicOperations.FixedTimeEquals(hash, keys.Current);
        var previous = keys.Previous is not null && CryptographicOperations.FixedTimeEquals(hash, keys.Previous);
        if (current)
        {
            return WebhookOriginMatch.Current;
        }

        return previous && keys.PreviousActive(now) ? WebhookOriginMatch.Previous : WebhookOriginMatch.Invalid;
    }

    public static byte[] Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
