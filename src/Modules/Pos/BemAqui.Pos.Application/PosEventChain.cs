using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace BemAqui.Pos.Application;

/// <summary>
/// Cadeia de hash dos eventos do caixa (OFF-02 CA 2 e 11). O aparelho calcula, para cada evento, o SHA-256 (hex minúsculo) do texto
/// <c>v1\n{eventId}\n{deviceId}\n{registerId}\n{deviceEpoch}\n{seq}\n{schemaVersion}\n{kind}\n{occurredAt}\n{prevHash}\n{SHA-256 hex do payload}</c>,
/// com o <c>occurredAt</c> e o <c>payload</c> exatamente como enviados (texto) e <c>prevHash</c> vazio no primeiro evento da época. O mesmo cálculo
/// roda em <c>src/Web/src/pdv/sync/event-chain.ts</c>. O CPF nunca entra no evento, então apagá-lo não quebra a cadeia (CA 17).
/// </summary>
public static partial class PosEventChain
{
    /// <summary>Versão atual do formato dos eventos do aparelho.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Prazo de suporte (ponto em aberto da story, proposta aceita: duas versões, a atual e a anterior).</summary>
    public const int SupportedVersions = 2;

    public static bool IsSupported(int schemaVersion) => schemaVersion >= 1 && schemaVersion <= CurrentSchemaVersion && schemaVersion > CurrentSchemaVersion - SupportedVersions;

    public static string Hash(Guid eventId, Guid deviceId, Guid registerId, int deviceEpoch, long seq, int schemaVersion, string kind, string occurredAt, string? prevHash, string payload)
    {
        var text = string.Join('\n',
            "v1",
            eventId.ToString("D"),
            deviceId.ToString("D"),
            registerId.ToString("D"),
            deviceEpoch.ToString(CultureInfo.InvariantCulture),
            seq.ToString(CultureInfo.InvariantCulture),
            schemaVersion.ToString(CultureInfo.InvariantCulture),
            kind,
            occurredAt,
            prevHash ?? string.Empty,
            Sha256Hex(payload));
        return Sha256Hex(text);
    }

    public static string Sha256Hex(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static bool IsHash(string? value) => value is { Length: 64 } && HexPattern().IsMatch(value);

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HexPattern();
}

/// <summary>
/// Texto livre que veio do aparelho (requisitos da PDV-03 e da CX-01): a mesma normalização do <c>looksLikeCardNumber</c> do aparelho (NFKC, sem os
/// caracteres <c>\p{Cf}</c>, qualquer não-alfanumérico entre dígitos tratado como separador) e recusa a sequência de 13 dígitos ou mais.
/// </summary>
public static partial class PosText
{
    public static bool LooksLikeCardNumber(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var normalized = FormatChars().Replace(text.Normalize(NormalizationForm.FormKC), string.Empty);
        var joined = Separators().Replace(normalized, "$1");
        return LongDigits().IsMatch(joined);
    }

    /// <summary>
    /// Tira do conteúdo todo texto que parece número de cartão (troca pelo nulo) antes de gravar o evento: o sistema nunca guarda número de cartão,
    /// nem no registro imutável. Devolve o texto novo e se algo saiu.
    /// </summary>
    public static (string Payload, bool Redacted) RedactCardNumbers(string payload)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(payload);
        }
        catch (System.Text.Json.JsonException)
        {
            return LooksLikeCardNumber(payload) ? ("{}", true) : (payload, false);
        }

        var redacted = Walk(root);
        return redacted ? (root?.ToJsonString() ?? "{}", true) : (payload, false);
    }

    private static bool Walk(JsonNode? node)
    {
        var found = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (!NotText(key) && obj[key] is JsonValue value && value.TryGetValue<string>(out var text) && !IdOrInstant(text) && LooksLikeCardNumber(text))
                    {
                        obj[key] = null;
                        found = true;
                    }
                    else
                    {
                        found |= Walk(obj[key]);
                    }
                }

                break;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is JsonValue value && value.TryGetValue<string>(out var text) && !IdOrInstant(text) && LooksLikeCardNumber(text))
                    {
                        array[i] = null;
                        found = true;
                    }
                    else
                    {
                        found |= Walk(array[i]);
                    }
                }

                break;
        }

        return found;
    }

    /// <summary>B1: todo campo de texto é conferido, menos os que não são texto livre: ids, instantes, número da venda e o GTIN (13 dígitos).</summary>
    private static readonly HashSet<string> NotTextKeys = new(StringComparer.Ordinal) { "id", "code", "at", "occurredAt", "printedAt", "number", "saleNumber", "quantity" };

    private static bool NotText(string key) => NotTextKeys.Contains(key) || key.EndsWith("Id", StringComparison.Ordinal);

    private static bool IdOrInstant(string text) =>
        Guid.TryParse(text, out _) || (text.Length >= 20 && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _));

    [GeneratedRegex(@"\p{Cf}")]
    private static partial Regex FormatChars();

    [GeneratedRegex(@"(\p{Nd})[^\p{L}\p{N}]+(?=\p{Nd})")]
    private static partial Regex Separators();

    [GeneratedRegex(@"\p{Nd}{13,}")]
    private static partial Regex LongDigits();
}
