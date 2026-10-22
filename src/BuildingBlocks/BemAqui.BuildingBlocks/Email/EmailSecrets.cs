using System.Text.Json;
using System.Text.Json.Nodes;

namespace BemAqui.BuildingBlocks.Email;

/// <summary>
/// Campos do modelo que carregam segredo (link com token de uso único), por template (D19). O <c>email_logs</c> guarda
/// o modelo só até o envio terminar: depois do envio, da falha definitiva ou do Skipped, esses campos são apagados.
/// Além dos campos declarados, qualquer valor que seja um link http(s) com <c>token=</c> na query também é apagado,
/// para um template novo com link de token não escapar por esquecimento.
/// Reenvio nunca reaproveita o modelo guardado; quem reenvia gera um token novo (ex.: <c>InviteService.SendAsync</c>).
/// </summary>
public static class EmailSecrets
{
    /// <summary>Valor que fica no lugar do segredo, para o suporte saber que havia um link ali.</summary>
    public const string Redacted = "[removido após o envio]";

    private static readonly Dictionary<string, string[]> Fields = new(StringComparer.Ordinal)
    {
        ["invite"] = ["link"],
        ["password-reset"] = ["link"],
    };

    /// <summary>Templates com campo secreto declarado (usado também pela limpeza dos registros antigos).</summary>
    public static IReadOnlyCollection<string> Templates => Fields.Keys;

    public static IReadOnlyCollection<string> FieldsOf(string template) =>
        Fields.TryGetValue(template, out var fields) ? fields : [];

    /// <summary>Link absoluto http(s) com parâmetro <c>token</c> na query.</summary>
    public static bool IsTokenLink(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && uri.Query.TrimStart('?').Split('&').Any(p => p.StartsWith("token=", StringComparison.OrdinalIgnoreCase));

    /// <summary>Modelo (JSON) sem os campos secretos; os demais campos ficam para o suporte.</summary>
    public static string Redact(string template, string modelJson)
    {
        if (JsonNode.Parse(modelJson) is not JsonObject model)
        {
            return modelJson;
        }

        var declared = FieldsOf(template);
        var secret = model
            .Where(p => declared.Contains(p.Key) || (p.Value is JsonValue v && v.TryGetValue<string>(out var s) && IsTokenLink(s)))
            .Select(p => p.Key)
            .ToList();
        if (secret.Count == 0)
        {
            return modelJson;
        }

        foreach (var field in secret)
        {
            model[field] = Redacted;
        }

        return model.ToJsonString(JsonSerializerOptions.Default);
    }
}
