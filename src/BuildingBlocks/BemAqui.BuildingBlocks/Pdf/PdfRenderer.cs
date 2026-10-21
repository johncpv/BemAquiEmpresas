using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.BuildingBlocks.Pdf;

/// <summary>Geração de PDF (ADR 0022): os layouts ficam nos módulos; trocar de biblioteca muda só o renderer e os layouts.</summary>
public interface IPdfRenderer
{
    Task<byte[]> RenderAsync(IDocument document, CancellationToken cancellationToken = default);
}

/// <summary>
/// QuestPDF com a licença Community (adendo da ADR 0022, P-21) e a Archivo embutida; <c>UseSystemFonts = false</c>, então
/// o PDF nunca depende de fonte instalada (a imagem Alpine não tem nenhuma).
/// </summary>
public sealed class QuestPdfRenderer : IPdfRenderer
{
    public const string FontFamily = "Archivo";

    private static readonly Lazy<bool> Configured = new(Configure);

    public QuestPdfRenderer() => _ = Configured.Value;

    public Task<byte[]> RenderAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(document.GeneratePdf, cancellationToken);
    }

    private static bool Configure()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = false;
        var assembly = typeof(QuestPdfRenderer).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            FontManager.RegisterFontFromStream(stream);
        }

        return true;
    }

    /// <summary>Para os testes: as fontes que o renderer embute.</summary>
    internal static IReadOnlyList<string> EmbeddedFonts() =>
        typeof(QuestPdfRenderer).Assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)).ToList();

}
