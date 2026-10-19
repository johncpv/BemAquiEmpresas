using System.Buffers.Binary;

namespace BemAqui.BuildingBlocks.Attachments;

/// <summary>Largura e altura de PNG, JPEG e WebP pelo cabeçalho, sem decodificar a imagem (CAD-06 CA 5 e 6).</summary>
public static class ImageInfo
{
    public static (int Width, int Height)? TryGetSize(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47]))
        {
            return (BinaryPrimitives.ReadInt32BigEndian(data[16..20]), BinaryPrimitives.ReadInt32BigEndian(data[20..24]));
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            return Jpeg(data);
        }

        if (data.Length >= 30 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return WebP(data);
        }

        return null;
    }

    private static (int, int)? Jpeg(ReadOnlySpan<byte> data)
    {
        var i = 2;
        while (i + 9 < data.Length)
        {
            if (data[i] != 0xFF)
            {
                return null;
            }

            var marker = data[i + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..]);
            // SOF0..SOF15, menos DHT (C4), JPG (C8) e DAC (CC): trazem altura e largura.
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..]));
            }

            i += 2 + length;
        }

        return null;
    }

    private static (int, int)? WebP(ReadOnlySpan<byte> d)
    {
        var chunk = d[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            return (BinaryPrimitives.ReadUInt16LittleEndian(d[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(d[28..]) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(d[21..]);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            return ((d[24] | (d[25] << 8) | (d[26] << 16)) + 1, (d[27] | (d[28] << 8) | (d[29] << 16)) + 1);
        }

        return null;
    }
}
