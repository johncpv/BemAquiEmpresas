using UglyToad.PdfPig;
using UglyToad.PdfPig.Tokens;

namespace BemAqui.PdfReader;

/// <summary>
/// Conteúdo ativo do PDF (FIS-05 CA 1): JavaScript, anexo embutido, formulário com ação (envio, importação), abertura de programa ou de
/// arquivo remoto, mídia e criptografia. A conferência percorre todos os objetos já decodificados pelo PdfPig (inclusive os de dentro de
/// object streams e os nomes escritos com <c>#xx</c>), sem executar nada. Qualquer achado recusa o PDF inteiro.
/// </summary>
internal static class PdfSafety
{
    /// <summary>Limite de objetos conferidos: PDF de nota fiscal tem poucas centenas; acima disso, recusado (bomba de objetos).</summary>
    public const int MaxObjects = 20_000;

    private const int MaxDepth = 24;

    /// <summary>Chaves de dicionário que só existem para conteúdo ativo ou embutido.</summary>
    private static readonly HashSet<string> DangerousKeys = new(StringComparer.Ordinal)
    {
        "JS", "JavaScript", "EmbeddedFile", "EmbeddedFiles", "RichMedia", "RichMediaContent", "XFA", "Launch",
    };

    /// <summary>Valores de nome (tipo de ação ou de objeto) recusados.</summary>
    private static readonly HashSet<string> DangerousNames = new(StringComparer.Ordinal)
    {
        "JavaScript", "Launch", "SubmitForm", "ImportData", "GoToR", "GoToE", "RichMedia", "EmbeddedFile", "Movie", "Sound", "Rendition", "Encrypt",
    };

    /// <summary>Motivo da recusa (para o log do worker, nunca o conteúdo) ou null se o PDF não tem conteúdo ativo.</summary>
    public static string? Inspect(PdfDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.IsEncrypted || document.Structure.Trailer.EncryptionToken is not null)
        {
            return "encrypted";
        }

        if (document.Advanced.TryGetEmbeddedFiles(out var files) && files.Count > 0)
        {
            return "embedded-file";
        }

        var count = 0;
        foreach (var reference in document.Structure.CrossReferenceTable.ObjectOffsets.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > MaxObjects)
            {
                return "too-many-objects";
            }

            ObjectToken? token;
            try
            {
                token = document.Structure.GetObject(reference);
            }
#pragma warning disable CA1031 // objeto quebrado não impede a conferência dos outros; a leitura do texto decide depois
            catch (Exception)
#pragma warning restore CA1031
            {
                continue;
            }

            if (token is not null && IsDangerous(token.Data, 0))
            {
                return "active-content";
            }
        }

        return IsDangerous(document.Structure.Catalog.CatalogDictionary, 0) ? "active-content" : null;
    }

    private static bool IsDangerous(IToken? token, int depth)
    {
        if (token is null || depth > MaxDepth)
        {
            return depth > MaxDepth;
        }

        switch (token)
        {
            case NameToken name:
                return DangerousNames.Contains(name.Data);
            case DictionaryToken dictionary:
                foreach (var (key, value) in dictionary.Data)
                {
                    if (DangerousKeys.Contains(key) || IsDangerous(value, depth + 1))
                    {
                        return true;
                    }
                }

                return false;
            case ArrayToken array:
                return array.Data.Any(t => IsDangerous(t, depth + 1));
            case StreamToken stream:
                return IsDangerous(stream.StreamDictionary, depth + 1);
            default:
                return false;
        }
    }
}
