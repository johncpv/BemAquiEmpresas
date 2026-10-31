using System.Xml;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>A nota lida e conferida: os dados e quem assinou.</summary>
public sealed record ValidatedNfe(ParsedNfe Nfe, NfeSigner Signer);

/// <summary>
/// A sequência de conferência do XML da nota de entrada (FIS-04a CA 1 e 2), na ordem: leitor seguro; nota sem o <c>nfeProc</c> (sem
/// protocolo) recusada; esquema oficial da NF-e 4.00; protocolo da SEFAZ (<c>cStat</c> 100 e a mesma chave); chave de acesso (dígito);
/// modelo 55 e ambiente de produção; emitente pessoa jurídica; assinatura e cadeia ICP-Brasil (<see cref="NfeSignatureValidator"/>). O
/// destinatário e a duplicidade são conferidos pelo serviço (dependem do tenant).
/// </summary>
public static class NfeXmlValidator
{
    public const int AuthorizedStatus = 100;

    public static Result<ValidatedNfe> Validate(byte[] content, INfeTrustAnchors anchors)
    {
        var loaded = NfeXmlReader.Load(content);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var document = loaded.Value;
        var root = document.DocumentElement;
        if (root is null || root.NamespaceURI != NfeXmlReader.NfeNamespace)
        {
            return NfeXmlReader.Invalid("incoming_xml_invalid");
        }

        if (root.LocalName == "NFe")
        {
            return NfeXmlReader.Invalid("incoming_xml_no_protocol", IncomingInvoiceRules.NoProtocolMessage);
        }

        if (root.LocalName != "nfeProc")
        {
            return NfeXmlReader.Invalid("incoming_xml_invalid");
        }

        if (!HasProtocol(document))
        {
            return NfeXmlReader.Invalid("incoming_xml_no_protocol", IncomingInvoiceRules.NoProtocolMessage);
        }

        if (NfeSchema.Validate(document).Count > 0)
        {
            return NfeXmlReader.Invalid("incoming_xml_schema", "Este arquivo não é um XML de NF-e válido: não segue o leiaute oficial da NF-e 4.00.");
        }

        Result<ParsedNfe> parsed;
        try
        {
            parsed = NfeXmlParser.Parse(document);
        }
        catch (Exception e) when (e is FormatException or OverflowException or System.Xml.XPath.XPathException)
        {
            return NfeXmlReader.Invalid("incoming_xml_invalid");
        }

        if (parsed.IsFailure)
        {
            return parsed.Error!;
        }

        var nfe = parsed.Value;
        if (nfe.ProtocolStatus != AuthorizedStatus || nfe.Protocol is not { Length: > 0 })
        {
            return NfeXmlReader.Invalid("incoming_xml_not_authorized", IncomingInvoiceRules.NoProtocolMessage);
        }

        if (!IncomingInvoiceRules.IsValidAccessKey(nfe.AccessKey) || !string.Equals(nfe.ProtocolKey, nfe.AccessKey, StringComparison.Ordinal))
        {
            return NfeXmlReader.Invalid("incoming_xml_protocol_key", "O protocolo de autorização não é desta nota. Peça ao fornecedor o XML autorizado.");
        }

        if (nfe.Model != 55)
        {
            return NfeXmlReader.Invalid("incoming_xml_model", "Só a NF-e (modelo 55) é importada aqui.");
        }

        if (nfe.Environment != 1 || nfe.ProtocolEnvironment != 1)
        {
            return NfeXmlReader.Invalid("incoming_xml_homologation", "Esta nota é de homologação (sem valor fiscal) e não é importada.");
        }

        if (nfe.IssuerCnpj is not { Length: 14 })
        {
            return NfeXmlReader.Invalid("incoming_xml_issuer_cpf", "Só a nota emitida por CNPJ é importada pelo XML.");
        }

        var signer = NfeSignatureValidator.Validate(document, anchors, nfe.IssuedAt);
        return signer.IsFailure ? signer.Error! : new ValidatedNfe(nfe, signer.Value);
    }

    private static bool HasProtocol(XmlDocument document) =>
        document.DocumentElement!.ChildNodes.OfType<XmlElement>().Any(e => e.LocalName == "protNFe" && e.NamespaceURI == NfeXmlReader.NfeNamespace);
}
