using System.Globalization;
using System.Text;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.BuildingBlocks.Pdf;

/// <summary>
/// QR do modelo único (EXP-03): desenhado pelo QRCoder (MIT, sem chamada externa), só com a URL <b>absoluta do host do tenant</b> (o
/// chamador monta pelo <c>TenantLinks</c>, nunca pelo host da requisição). O QR só carrega o endereço com o id: abrir continua exigindo
/// login e permissão.
/// </summary>
public static class PdfQr
{
    /// <summary>Desenha o QR quadrado de <paramref name="size"/> pontos.</summary>
    public static void Compose(IContainer container, string absoluteUrl, float size)
    {
        ArgumentNullException.ThrowIfNull(container);
        container.Width(size).Height(size).Svg(Svg(absoluteUrl)).FitArea();
    }

    /// <summary>SVG do QR: correção M, zona de silêncio de 4 módulos e um retângulo por trecho escuro de cada linha. Só aceita https absoluto.</summary>
    public static string Svg(string absoluteUrl)
    {
        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("O QR do PDF leva só uma URL https absoluta do tenant.", nameof(absoluteUrl));
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(absoluteUrl, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var size = matrix.Count;
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {size} {size}\" width=\"{size}\" height=\"{size}\" shape-rendering=\"crispEdges\">");
        svg.Append(CultureInfo.InvariantCulture, $"<rect width=\"{size}\" height=\"{size}\" fill=\"#ffffff\"/><path fill=\"#000000\" d=\"");
        for (var y = 0; y < size; y++)
        {
            var x = 0;
            while (x < size)
            {
                if (!matrix[y][x])
                {
                    x++;
                    continue;
                }

                var start = x;
                while (x < size && matrix[y][x])
                {
                    x++;
                }

                svg.Append(CultureInfo.InvariantCulture, $"M{start} {y}h{x - start}v1h{start - x}z");
            }
        }

        svg.Append("\"/></svg>");
        return svg.ToString();
    }
}

/// <summary>
/// Indicador de etapa impresso (EXP-03), versão de papel do componente do app no estilo display de andar: a letra da etapa atual em âmbar
/// sobre o fundo escuro e um segmento por etapa (cheio as concluídas e a atual, vazado as seguintes). Em preto e branco continua legível:
/// a letra é texto e os segmentos diferem por preenchimento, não só por cor.
/// </summary>
public static class PdfStageIndicator
{
    /// <param name="letters">A letra de cada etapa, em ordem.</param>
    /// <param name="current">Índice da etapa atual; -1 sem etapa (mostra "—"); igual ao total quando todas estão concluídas (a última letra, tudo cheio).</param>
    public static void Compose(IContainer container, IReadOnlyList<string> letters, int current)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(letters);
        var letter = current < 0 || letters.Count == 0 ? "—" : letters[Math.Min(current, letters.Count - 1)];
        container.Row(row =>
        {
            row.AutoItem().Background(PdfTheme.Rail).PaddingVertical(2).PaddingHorizontal(7).AlignMiddle().Text(letter).FontSize(16).Bold().FontColor(PdfTheme.Amber);
            row.ConstantItem(6);
            row.AutoItem().AlignMiddle().Row(segments =>
            {
                for (var i = 0; i < letters.Count; i++)
                {
                    var segment = segments.ConstantItem(12).Height(8).Border(1).BorderColor(PdfTheme.Rail);
                    if (i < current)
                    {
                        segment.Background(PdfTheme.Rail);
                    }
                    else if (i == current)
                    {
                        segment.Background(PdfTheme.Amber);
                    }

                    segments.ConstantItem(2);
                }
            });
        });
    }
}
