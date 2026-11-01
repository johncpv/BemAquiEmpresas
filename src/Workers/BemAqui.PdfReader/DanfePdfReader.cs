using System.Diagnostics;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace BemAqui.PdfReader;

/// <summary>
/// Leitura do PDF da DANFE (FIS-05 CA 1 a 4): tamanho, cabeçalho <c>%PDF-</c>, criptografia e conteúdo ativo (<see cref="PdfSafety"/>), de
/// 1 a 10 páginas, texto (sem OCR: PDF só de imagem é <c>NoText</c>) e os campos (<see cref="DanfeParser"/>). Nada do PDF é executado nem
/// gravado em disco; o texto só existe na memória desta chamada.
/// </summary>
internal static class DanfePdfReader
{
    public const int MaxPages = 10;

    /// <summary>Menos letras que isso no PDF inteiro: tratado como sem texto (escaneado).</summary>
    public const int MinLetters = 20;

    public static ReadResponse Read(byte[] pdf, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        var clock = Stopwatch.StartNew();
        int Ms() => (int)Math.Min(int.MaxValue, clock.ElapsedMilliseconds);
        if (pdf.Length == 0 || pdf.Length > ReaderWire.MaxRequestBytes || !pdf.AsSpan().StartsWith("%PDF-"u8))
        {
            return ReadResponse.Of("Invalid", null, Ms());
        }

        PdfDocument document;
        try
        {
            document = PdfDocument.Open(pdf, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true, ClipPaths = false });
        }
        catch (PdfDocumentEncryptedException)
        {
            return ReadResponse.Of("Unsafe", null, Ms());
        }
#pragma warning disable CA1031 // PDF que não abre é "Invalid", qualquer que seja o motivo do PdfPig
        catch (Exception)
#pragma warning restore CA1031
        {
            return ReadResponse.Of("Invalid", null, Ms());
        }

        using (document)
        {
            if (PdfSafety.Inspect(document, cancellationToken) is not null)
            {
                return ReadResponse.Of("Unsafe", document.NumberOfPages, Ms());
            }

            var pages = document.NumberOfPages;
            if (pages is < 1 or > MaxPages)
            {
                return ReadResponse.Of("TooManyPages", pages, Ms());
            }

            var words = new List<PdfWord>();
            var letters = 0;
            for (var number = 1; number <= pages; number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(number);
                letters += page.Letters.Count;
                words.AddRange(page.GetWords().Select(w => new PdfWord(w.Text, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom, w.BoundingBox.Top, number)));
            }

            if (letters < MinLetters)
            {
                return ReadResponse.Of("NoText", pages, Ms());
            }

            cancellationToken.ThrowIfCancellationRequested();
            var fields = DanfeParser.Parse(words);
            return new ReadResponse("Read", pages, Ms(), fields);
        }
    }
}
