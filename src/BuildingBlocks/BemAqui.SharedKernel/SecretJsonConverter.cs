using System.Text.Json;
using System.Text.Json.Serialization;

namespace BemAqui.SharedKernel;

/// <summary>
/// Lê o segredo de um DTO de escrita (string → <see cref="Secret"/>). Serializar um <see cref="Secret"/> é bug
/// (segredo é write-only, ADR 0007): o converter lança em vez de emitir qualquer coisa. A máscara <c>***</c>
/// recebida de volta num PUT é recusada, para não substituir o segredo real por ela.
/// </summary>
public sealed class SecretJsonConverter : JsonConverter<Secret>
{
    public override Secret? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var value = reader.GetString();
        if (value == Secret.Mask)
        {
            throw new JsonException("Valor mascarado ('***') não é um segredo: envie o valor novo ou omita o campo.");
        }

        return string.IsNullOrEmpty(value) ? null : new Secret(value);
    }

    public override void Write(Utf8JsonWriter writer, Secret value, JsonSerializerOptions options) =>
        throw new InvalidOperationException("Segredo é write-only: nenhuma resposta pode serializar um Secret (ADR 0007).");
}
