using System.Buffers.Binary;

namespace BemAqui.BuildingBlocks.Attachments;

/// <summary>
/// Passo do pipeline de imagem (PAT-01a CA 8, revisão pré-Fases 4 a 8 §4.3): regrava PNG, JPEG e WebP sem os metadados (EXIF, GPS,
/// XMP, IPTC, comentários e textos), sem decodificar a imagem. No JPEG, a orientação da câmera é mantida num EXIF mínimo, só com a
/// tag de orientação, para a foto não aparecer deitada; nada mais do EXIF original sobrevive. Arquivo que não dá para percorrer
/// (estrutura quebrada) devolve <c>null</c> e é recusado: nunca é guardado com os metadados.
/// </summary>
public static class ImageMetadata
{
    public static bool Supports(string contentType) => contentType is "image/jpeg" or "image/png" or "image/webp";

    /// <summary>A imagem sem metadados, ou <c>null</c> se o tipo não é suportado ou a estrutura está quebrada.</summary>
    public static byte[]? Strip(ReadOnlySpan<byte> data, string contentType)
    {
        try
        {
            return contentType switch
            {
                "image/jpeg" => Jpeg(data),
                "image/png" => Png(data),
                "image/webp" => WebP(data),
                _ => null,
            };
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    // JPEG: mantém APP0 (JFIF), APP2 (perfil de cor ICC) e APP14 (Adobe); descarta APP1 (EXIF/XMP), APP3 a APP13 (IPTC), APP15 e COM.
    private static byte[]? Jpeg(ReadOnlySpan<byte> d)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8)
        {
            return null;
        }

        using var output = new MemoryStream(d.Length);
        output.Write([0xFF, 0xD8]);
        var orientationWritten = false;
        ushort? orientation = null;
        var i = 2;
        while (i < d.Length)
        {
            if (d[i] != 0xFF)
            {
                return null;
            }

            var marker = d[i + 1];
            if (marker == 0xFF)
            {
                i++; // byte de preenchimento
                continue;
            }

            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                output.Write(d.Slice(i, 2));
                i += 2;
                continue;
            }

            if (marker == 0xD9)
            {
                output.Write(d.Slice(i, 2));
                return output.ToArray();
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(d[(i + 2)..]);
            if (length < 2 || i + 2 + length > d.Length)
            {
                return null;
            }

            var segment = d.Slice(i, 2 + length);
            var payload = segment[4..];
            if (marker == 0xE1)
            {
                orientation ??= ExifOrientation(payload);
            }
            else if (marker is 0xE0 or 0xE2 or 0xEE || marker < 0xE0 || marker is > 0xEF and not 0xFE)
            {
                // Antes do primeiro segmento que não é APPn (DQT, SOF, DHT…), grava a orientação mínima depois do JFIF.
                if (!orientationWritten && marker is not (0xE0 or 0xE2 or 0xEE) && orientation is { } o and not 1)
                {
                    output.Write(MinimalExif(o));
                }

                if (marker is not (0xE0 or 0xE2 or 0xEE))
                {
                    orientationWritten = true;
                }

                output.Write(segment);
                if (marker == 0xDA)
                {
                    // Início da imagem comprimida: o resto vai como está (marcadores dentro dela não são metadados).
                    output.Write(d[(i + 2 + length)..]);
                    return output.ToArray();
                }
            }

            i += 2 + length;
        }

        return null;
    }

    private static ushort? ExifOrientation(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 14 || !payload.StartsWith("Exif\0\0"u8))
        {
            return null;
        }

        var tiff = payload[6..];
        var little = tiff[0] == (byte)'I' && tiff[1] == (byte)'I';
        if (!little && !(tiff[0] == (byte)'M' && tiff[1] == (byte)'M'))
        {
            return null;
        }

        var offset = U32(tiff, 4, little);
        if (offset < 8 || offset > int.MaxValue || (int)offset + 2 > tiff.Length)
        {
            return null;
        }

        var ifd = (int)offset;
        var count = U16(tiff, ifd, little);
        for (var n = 0; n < count && ifd + 2 + (n * 12) + 12 <= tiff.Length; n++)
        {
            var entry = ifd + 2 + (n * 12);
            if (U16(tiff, entry, little) == 0x0112)
            {
                var value = U16(tiff, entry + 8, little);
                return value is >= 1 and <= 8 ? value : null;
            }
        }

        return null;
    }

    private static uint U32(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

    private static ushort U16(ReadOnlySpan<byte> data, int at, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(data[at..]) : BinaryPrimitives.ReadUInt16BigEndian(data[at..]);

    // APP1 com um TIFF big-endian e um IFD de uma entrada só: Orientation (0x0112, SHORT, 1).
    private static byte[] MinimalExif(ushort orientation) =>
    [
        0xFF, 0xE1, 0x00, 0x22,
        (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
        (byte)'M', (byte)'M', 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
        0x00, 0x01,
        0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, (byte)(orientation >> 8), (byte)orientation, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00,
    ];

    private static readonly string[] PngMetadataChunks = ["eXIf", "tEXt", "zTXt", "iTXt", "tIME"];

    private static byte[]? Png(ReadOnlySpan<byte> d)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (!d.StartsWith(signature))
        {
            return null;
        }

        using var output = new MemoryStream(d.Length);
        output.Write(signature);
        var i = signature.Length;
        while (i + 12 <= d.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(d[i..]);
            if (length < 0 || i + 12 + length > d.Length)
            {
                return null;
            }

            var type = System.Text.Encoding.ASCII.GetString(d.Slice(i + 4, 4));
            if (!PngMetadataChunks.Contains(type))
            {
                output.Write(d.Slice(i, 12 + length));
            }

            i += 12 + length;
            if (type == "IEND")
            {
                return output.ToArray();
            }
        }

        return null;
    }

    private static byte[]? WebP(ReadOnlySpan<byte> d)
    {
        if (d.Length < 12 || !d.StartsWith("RIFF"u8) || !d[8..12].SequenceEqual("WEBP"u8))
        {
            return null;
        }

        var end = Math.Min(d.Length, 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(d[4..]));
        var chunks = new List<byte[]>();
        var i = 12;
        while (i + 8 <= end)
        {
            var fourcc = System.Text.Encoding.ASCII.GetString(d.Slice(i, 4));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(d[(i + 4)..]);
            var total = 8 + size + (size % 2);
            if (size < 0 || i + 8 + size > end)
            {
                return null;
            }

            if (fourcc is not ("EXIF" or "XMP "))
            {
                var chunk = d.Slice(i, Math.Min(total, end - i)).ToArray();
                if (fourcc == "VP8X" && chunk.Length > 8)
                {
                    chunk[8] &= unchecked((byte)~0x0C); // tira as marcas de EXIF (0x08) e XMP (0x04)
                }

                chunks.Add(chunk);
            }

            i += total;
        }

        if (chunks.Count == 0)
        {
            return null;
        }

        var body = chunks.Sum(c => c.Length);
        using var output = new MemoryStream(12 + body);
        output.Write("RIFF"u8);
        Span<byte> size32 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size32, (uint)(4 + body));
        output.Write(size32);
        output.Write("WEBP"u8);
        foreach (var chunk in chunks)
        {
            output.Write(chunk);
        }

        return output.ToArray();
    }
}
