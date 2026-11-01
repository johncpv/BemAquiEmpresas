using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BemAqui.PdfReader;

/// <summary>Campo lido: chave do formulário, valor normalizado (data ISO, número com ponto) e situação (<c>Read</c>, <c>Uncertain</c>, <c>NotFound</c>).</summary>
internal sealed record ReadField(string Key, string? Value, string State);

/// <summary>
/// Resposta do worker para a API: o resultado (<c>Read</c>, <c>NoText</c>, <c>Unsafe</c>, <c>TooManyPages</c>, <c>Invalid</c>, <c>Failed</c>,
/// <c>TimedOut</c>), as páginas, a duração e os campos. Nunca leva o texto extraído do PDF (FIS-05 CA 12).
/// </summary>
internal sealed record ReadResponse(string Result, int? Pages, int DurationMs, IReadOnlyList<ReadField> Fields)
{
    public static ReadResponse Of(string result, int? pages, int durationMs) => new(result, pages, durationMs, []);
}

/// <summary>
/// Protocolo do socket Unix entre a API e o worker: cada lado manda 4 bytes (tamanho, big-endian) e o conteúdo. A API manda o PDF (até
/// <see cref="MaxRequestBytes"/>); o worker responde o JSON (até <see cref="MaxResponseBytes"/>). Uma leitura por conexão.
/// </summary>
internal static class ReaderWire
{
    public const int MaxRequestBytes = 5 * 1024 * 1024;
    public const int MaxResponseBytes = 2 * 1024 * 1024;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Lê um quadro; tamanho zero, negativo ou acima do limite é recusado sem ler o resto (null).</summary>
    public static async Task<byte[]?> ReadFrameAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > maxBytes)
        {
            return null;
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return payload;
    }

    public static byte[] Serialize(ReadResponse response) => JsonSerializer.SerializeToUtf8Bytes(response, Json);
}
