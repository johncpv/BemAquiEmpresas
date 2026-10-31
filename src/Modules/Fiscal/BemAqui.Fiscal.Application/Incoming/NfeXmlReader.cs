using System.Xml;
using System.Xml.Schema;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>
/// Leitor seguro do XML da nota de entrada (FIS-04a CA 1, revisão pré-Fase 0 §3): <c>DtdProcessing.Prohibit</c> (DOCTYPE recusado, logo sem
/// entidade externa nem "billion laughs"), <c>XmlResolver = null</c> (nada é buscado fora), limite de caracteres e de profundidade. O
/// documento sai com os espaços preservados (a assinatura é sobre o texto exato). Qualquer falha vira "Este arquivo não é um XML de NF-e
/// válido.", sem repetir o conteúdo.
/// </summary>
public static class NfeXmlReader
{
    public const string NfeNamespace = "http://www.portalfiscal.inf.br/nfe";
    public const string DsigNamespace = "http://www.w3.org/2000/09/xmldsig#";

    /// <summary>Profundidade máxima de elementos (a NF-e real não passa de 12).</summary>
    public const int MaxDepth = 32;

    /// <summary>Caracteres do documento: o arquivo tem até 2 MB.</summary>
    public const long MaxCharacters = 2L * 1024 * 1024 + 1024;

    public static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxCharacters,
        MaxCharactersFromEntities = MaxCharacters, // sem DTD não há entidade declarada; o teto vale para as predefinidas (&amp;amp;)
        IgnoreProcessingInstructions = true,
        CloseInput = false,
    };

    /// <summary>Lê o arquivo; DOCTYPE, entidade, profundidade acima do limite, tamanho ou XML malformado: <see cref="IncomingInvoiceRules.InvalidXmlMessage"/>.</summary>
    public static Result<XmlDocument> Load(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length == 0 || content.Length > IncomingInvoiceRules.MaxBytes)
        {
            return Invalid("incoming_xml_invalid");
        }

        try
        {
            // 1ª passada: só a forma (profundidade); nada é guardado.
            using (var stream = new MemoryStream(content, writable: false))
            using (var reader = XmlReader.Create(stream, Settings()))
            {
                while (reader.Read())
                {
                    if (reader.Depth > MaxDepth)
                    {
                        return Invalid("incoming_xml_too_deep");
                    }
                }
            }

            // 2ª passada: o documento, com os espaços preservados para a assinatura.
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            using (var stream = new MemoryStream(content, writable: false))
            using (var reader = XmlReader.Create(stream, Settings()))
            {
                document.Load(reader);
            }

            return document;
        }
        catch (XmlException)
        {
            return Invalid("incoming_xml_invalid");
        }
    }

    public static Error Invalid(string code, string message = IncomingInvoiceRules.InvalidXmlMessage) =>
        Error.Validation(code, message, new Dictionary<string, string[]> { ["file"] = [message] });
}

/// <summary>
/// Esquema oficial da NF-e 4.00 (pacote PL_010_V1, com os grupos da reforma tributária), embutido no assembly: as inclusões são resolvidas
/// só entre os arquivos embutidos, nunca pela rede nem pelo disco. Valida o <c>nfeProc</c> inteiro (nota, assinatura e protocolo).
/// </summary>
public static class NfeSchema
{
    private const string Prefix = "BemAqui.Fiscal.Incoming.";
    private static readonly Lazy<XmlSchemaSet> Set = new(Build, LazyThreadSafetyMode.ExecutionAndPublication);

    public static XmlSchemaSet Schemas => Set.Value;

    /// <summary>Erros do esquema (até 3, sem o conteúdo do arquivo); vazio = válido.</summary>
    public static IReadOnlyList<string> Validate(XmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = new List<string>();
        var copy = (XmlDocument)document.CloneNode(true);
        copy.Schemas = Schemas;
        copy.Validate((_, e) =>
        {
            if (e.Severity == XmlSeverityType.Error && errors.Count < 3)
            {
                errors.Add(e.Message);
            }
        });
        return errors;
    }

    private static XmlSchemaSet Build()
    {
        var set = new XmlSchemaSet { XmlResolver = new EmbeddedResolver() };
        using (var stream = Open("procNFe_v4.00.xsd"))
        using (var reader = XmlReader.Create(stream, NfeXmlReader.Settings()))
        {
            set.Add(XmlSchema.Read(reader, null)!);
        }

        set.Compile();
        return set;
    }

    internal static Stream Open(string fileName) =>
        typeof(NfeSchema).Assembly.GetManifestResourceStream(Prefix + fileName) ?? throw new InvalidOperationException($"Esquema {fileName} não embutido.");

    /// <summary>Resolve só os arquivos embutidos pelo nome (<c>schemaLocation</c>); qualquer outro endereço é recusado.</summary>
    private sealed class EmbeddedResolver : XmlResolver
    {
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            ArgumentNullException.ThrowIfNull(absoluteUri);
            var name = Path.GetFileName(absoluteUri.IsAbsoluteUri ? absoluteUri.LocalPath : absoluteUri.OriginalString);
            if (name is not ("procNFe_v4.00.xsd" or "leiauteNFe_v4.00.xsd" or "tiposBasico_v4.00.xsd" or "xmldsig-core-schema_v1.01.xsd" or "DFeTiposBasicos_v1.00.xsd"))
            {
                throw new XmlException("Esquema fora dos embutidos.");
            }

            return Open(name);
        }
    }
}
