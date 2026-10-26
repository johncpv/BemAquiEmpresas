using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BemAqui.SharedKernel;

namespace BemAqui.BuildingBlocks.Webhooks;

/// <summary>
/// Corpo do webhook mascarado para "Ver conteúdo" (FND-16 CA 15, revisão §4.4): CPF/CNPJ, nome, e-mail, telefone, endereço e
/// chave Pix viram <see cref="Mask"/>; valores, ids e datas ficam visíveis. Mascara pelo nome do campo (e tudo abaixo dele) e
/// pelo formato do valor (e-mail, CPF/CNPJ válido, telefone com DDD), em qualquer lugar do JSON. Corpo que não é JSON não é
/// exibido.
/// </summary>
public static partial class JsonPersonalDataMask
{
    public const string Mask = "***";
    public const string NotJson = "O conteúdo não é JSON e não é exibido.";

    /// <summary>Trechos de nome de campo (sem acento, sem "_" e "-", minúsculas) que são dado pessoal.</summary>
    private static readonly string[] PersonalKeys =
    [
        "cpf", "cnpj", "cpfcnpj", "documento", "document", "taxid", "rg", "nome", "name", "razaosocial", "fantasia", "email", "mail",
        "telefone", "phone", "celular", "fone", "whatsapp", "endereco", "address", "logradouro", "street", "rua", "bairro", "district",
        "complemento", "cep", "zipcode", "postalcode", "pix", "chavepix", "txid", "pagador", "payer", "sacado", "beneficiario",
        "destinatario", "recipient", "contato", "contact", "ip",
    ];

    /// <summary>Campos que contêm um trecho acima mas não são pessoais (ex.: "nomeArquivo" não; "tipoEvento" não tem trecho).</summary>
    private static readonly string[] SafeKeys = ["filename", "nomearquivo", "eventname", "nomeevento", "hostname", "username"];

    public static string MaskJson(ReadOnlySpan<byte> body)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body.ToArray(), documentOptions: new JsonDocumentOptions { MaxDepth = 64, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            return NotJson;
        }

        var masked = Walk(root, personal: false);
        return masked?.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null";
    }

    public static bool IsPersonalKey(string key)
    {
        var normalized = Normalize(key);
        if (SafeKeys.Contains(normalized, StringComparer.Ordinal))
        {
            return false;
        }

        // "ip" e "rg" só como nome inteiro (senão "tipo" e "cargo" cairiam).
        return PersonalKeys.Any(p => p.Length <= 2 ? normalized == p : normalized.Contains(p, StringComparison.Ordinal));
    }

    public static bool IsPersonalValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var text = value.Trim();
        if (text.Length is < 6 or > 254)
        {
            return false;
        }

        if (Email().IsMatch(text))
        {
            return true;
        }

        if (DocumentLike().IsMatch(text) && BrazilianDocuments.IsValid(text))
        {
            return true;
        }

        return Phone().IsMatch(text) && text.Count(char.IsAsciiDigit) is >= 10 and <= 13;
    }

    /// <summary>Texto livre (ex.: observação): e-mail, CPF/CNPJ com máscara e telefone com DDD no meio da frase viram <see cref="Mask"/>.</summary>
    public static string MaskInline(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return InlinePersonal().Replace(value, Mask);
    }

    private static JsonNode? Walk(JsonNode? node, bool personal)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj.ToList())
                {
                    obj[key] = Walk(child?.DeepClone(), personal || IsPersonalKey(key));
                }

                return obj;
            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    array[i] = Walk(array[i]?.DeepClone(), personal);
                }

                return array;
            case JsonValue value:
                if (personal)
                {
                    return value.GetValueKind() is JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False ? value : JsonValue.Create(Mask);
                }

                if (value.GetValueKind() != JsonValueKind.String)
                {
                    return value;
                }

                var text = value.GetValue<string>();
                if (IsPersonalValue(text))
                {
                    return JsonValue.Create(Mask);
                }

                var inline = MaskInline(text);
                return string.Equals(inline, text, StringComparison.Ordinal) ? value : JsonValue.Create(inline);
            default:
                return node;
        }
    }

    private static string Normalize(string key)
    {
        var decomposed = key.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c is not '_' and not '-' and not ' ')
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"^[0-9A-Za-z./\-]{11,18}$", RegexOptions.CultureInvariant)]
    private static partial Regex DocumentLike();

    [GeneratedRegex(@"^(\+\d{1,3}\s?)?\(\d{2}\)\s?\d{4,5}-?\d{4}$|^\+\d{12,13}$", RegexOptions.CultureInvariant)]
    private static partial Regex Phone();

    [GeneratedRegex(@"[^@\s""'<>(),;:]+@[^@\s""'<>(),;:]+\.[A-Za-z]{2,}|\d{3}\.\d{3}\.\d{3}-\d{2}|\d{2}\.\d{3}\.\d{3}/\d{4}-\d{2}|\(\d{2}\)\s?\d{4,5}-?\d{4}", RegexOptions.CultureInvariant)]
    private static partial Regex InlinePersonal();
}
