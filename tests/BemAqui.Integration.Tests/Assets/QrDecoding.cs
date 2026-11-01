using System.Buffers.Binary;
using System.IO.Compression;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace BemAqui.Integration.Tests.Assets;

/// <summary>
/// Decodifica o QR de um documento do QuestPDF (PAT-01b CA 2): rasteriza a primeira página em PNG, lê o PNG (8 bits, sem entrelaçamento)
/// para tons de cinza e decodifica com o ZXing.Net (só nos testes).
/// </summary>
internal static class QrDecoding
{
    public static string Decode(IDocument document)
    {
        var png = document.GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 400 }).First();
        var (width, height, gray) = Gray(png);
        var source = new RGBLuminanceSource(gray, width, height, RGBLuminanceSource.BitmapFormat.Gray8);
        var hints = new Dictionary<DecodeHintType, object> { [DecodeHintType.TRY_HARDER] = true };
        var result = new QRCodeReader().decode(new BinaryBitmap(new HybridBinarizer(source)), hints)
            ?? throw new InvalidOperationException("QR code não encontrado na imagem da etiqueta.");
        return result.Text;
    }

    private static (int Width, int Height, byte[] Gray) Gray(byte[] png)
    {
        var position = 8;
        int width = 0, height = 0, colorType = 0;
        using var idat = new MemoryStream();
        while (position < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position));
            var type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            var data = png.AsSpan(position + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                if (data[8] != 8 || data[12] != 0)
                {
                    throw new NotSupportedException("PNG de teste precisa ter 8 bits e não ser entrelaçado.");
                }

                colorType = data[9];
            }
            else if (type == "IDAT")
            {
                idat.Write(data);
            }

            position += 12 + length;
        }

        var channels = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => throw new NotSupportedException("Tipo de cor do PNG: " + colorType) };
        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        var raw = inflated.ToArray();
        var stride = width * channels;
        var previous = new byte[stride];
        var current = new byte[stride];
        var gray = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            var offset = y * (stride + 1);
            var filter = raw[offset];
            for (var x = 0; x < stride; x++)
            {
                var value = raw[offset + 1 + x];
                var left = x >= channels ? current[x - channels] : 0;
                var up = previous[x];
                var upLeft = x >= channels ? previous[x - channels] : 0;
                current[x] = filter switch
                {
                    0 => value,
                    1 => (byte)(value + left),
                    2 => (byte)(value + up),
                    3 => (byte)(value + ((left + up) / 2)),
                    4 => (byte)(value + Paeth(left, up, upLeft)),
                    _ => throw new NotSupportedException("Filtro do PNG: " + filter),
                };
            }

            for (var x = 0; x < width; x++)
            {
                var p = x * channels;
                var luma = channels >= 3 ? ((current[p] * 299) + (current[p + 1] * 587) + (current[p + 2] * 114)) / 1000 : current[p];
                var alpha = channels is 2 or 4 ? current[p + channels - 1] : 255;
                gray[(y * width) + x] = (byte)(((luma * alpha) + (255 * (255 - alpha))) / 255);
            }

            (previous, current) = (current, previous);
        }

        return (width, height, gray);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
