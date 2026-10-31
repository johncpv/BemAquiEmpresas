using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using BemAqui.Fiscal.Application.Certificates;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>
/// Âncoras da cadeia ICP-Brasil para conferir o certificado da assinatura sem rede (FIS-04a CA 2): as raízes (confiança) e as AC
/// intermediárias (só para montar a cadeia, a NF-e traz só o certificado do emitente).
/// </summary>
public interface INfeTrustAnchors
{
    X509Certificate2Collection Roots { get; }

    X509Certificate2Collection Intermediates { get; }
}

/// <summary>
/// A cadeia ICP-Brasil embutida (<c>icp-brasil.pem</c>, pacote ACcompactado.zip do ITI: raízes v5, v6, v7, v10, v11 e v12 e as AC
/// intermediárias). Atualizada pela story que atualizar o XSD.
/// </summary>
public sealed class IcpBrasilTrustAnchors : INfeTrustAnchors
{
    private static readonly Lazy<(X509Certificate2Collection Roots, X509Certificate2Collection Intermediates)> Bundle = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public X509Certificate2Collection Roots => Bundle.Value.Roots;

    public X509Certificate2Collection Intermediates => Bundle.Value.Intermediates;

    private static (X509Certificate2Collection, X509Certificate2Collection) Load()
    {
        using var stream = NfeSchema.Open("icp-brasil.pem");
        using var text = new StreamReader(stream);
        var all = new X509Certificate2Collection();
        all.ImportFromPem(text.ReadToEnd());
        var roots = new X509Certificate2Collection();
        var intermediates = new X509Certificate2Collection();
        foreach (var certificate in all)
        {
            (certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData) ? roots : intermediates).Add(certificate);
        }

        return (roots, intermediates);
    }
}

/// <summary>Quem assinou: o CNPJ do certificado e o titular.</summary>
public sealed record NfeSigner(string Document, string Subject);

/// <summary>
/// Conferência estrutural e da assinatura da NF-e (FIS-04a CA 2, D12): exatamente um <c>infNFe</c> e exatamente uma <c>Signature</c> na
/// nota (filha de <c>NFe</c>), com uma referência só, cuja <c>URI</c> é <c>#</c> + o <c>Id</c> do <c>infNFe</c>; só as transformações
/// enveloped e C14N; a referência é resolvida só para aquele elemento (contra assinatura deslocada); digest e assinatura batendo; a cadeia
/// do certificado até uma raiz ICP-Brasil embutida, sem download nem consulta de revogação, na data de emissão; e o CNPJ do certificado
/// igual ao do emitente. A assinatura opcional da SEFAZ no <c>protNFe</c> não é conferida aqui (o protocolo é conferido pela chave e pelo
/// <c>cStat</c>).
/// </summary>
public static class NfeSignatureValidator
{
    private const string Enveloped = "http://www.w3.org/2000/09/xmldsig#enveloped-signature";
    private const string C14N = "http://www.w3.org/TR/2001/REC-xml-c14n-20010315";
    private static readonly string[] SignatureMethods = ["http://www.w3.org/2000/09/xmldsig#rsa-sha1", "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256"];
    private static readonly string[] DigestMethods = ["http://www.w3.org/2000/09/xmldsig#sha1", "http://www.w3.org/2001/04/xmlenc#sha256"];

    public static Result<NfeSigner> Validate(XmlDocument document, INfeTrustAnchors anchors, DateTimeOffset signedAt)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(anchors);
        var infs = document.GetElementsByTagName("infNFe", NfeXmlReader.NfeNamespace);
        if (infs.Count != 1 || infs[0] is not XmlElement inf || inf.ParentNode is not XmlElement nfe || nfe.LocalName != "NFe" || nfe.NamespaceURI != NfeXmlReader.NfeNamespace)
        {
            return Mismatch("incoming_xml_inf_count");
        }

        // Toda Signature fora do protNFe conta: tem de haver exatamente uma, filha do NFe.
        var signatures = document.GetElementsByTagName("Signature", NfeXmlReader.DsigNamespace).OfType<XmlElement>()
            .Where(s => !(s.ParentNode is XmlElement p && p.LocalName == "protNFe" && p.NamespaceURI == NfeXmlReader.NfeNamespace)).ToList();
        if (signatures.Count != 1 || !ReferenceEquals(signatures[0].ParentNode, nfe))
        {
            return Mismatch("incoming_xml_signature_count");
        }

        var id = inf.GetAttribute("Id");
        if (string.IsNullOrEmpty(id) || inf.Attributes.Cast<XmlAttribute>().Count(a => a.LocalName.Equals("Id", StringComparison.OrdinalIgnoreCase)) != 1)
        {
            return Mismatch("incoming_xml_inf_id");
        }

        var signed = new SingleIdSignedXml(document, inf, id);
        try
        {
            signed.LoadXml(signatures[0]);
        }
        catch (CryptographicException)
        {
            return Mismatch("incoming_xml_signature_malformed");
        }

        var info = signed.SignedInfo;
        if (info is null || info.References.Count != 1 || info.References[0] is not Reference reference || reference.Uri != "#" + id
            || info.CanonicalizationMethod != C14N || !SignatureMethods.Contains(info.SignatureMethod) || !DigestMethods.Contains(reference.DigestMethod))
        {
            return Mismatch("incoming_xml_reference");
        }

        var transforms = new List<string>();
        for (var i = 0; i < reference.TransformChain.Count; i++)
        {
            transforms.Add(reference.TransformChain[i].Algorithm ?? string.Empty);
        }

        if (transforms.Count == 0 || transforms.Any(t => t is not (Enveloped or C14N)) || transforms.Count != transforms.Distinct(StringComparer.Ordinal).Count())
        {
            return Mismatch("incoming_xml_transforms");
        }

        var certificates = signed.KeyInfo?.OfType<KeyInfoX509Data>().SelectMany(d => d.Certificates?.OfType<X509Certificate2>() ?? []).ToList() ?? [];
        if (certificates.Count != 1)
        {
            return Mismatch("incoming_xml_certificate");
        }

        var certificate = certificates[0];
        bool valid;
        try
        {
            valid = signed.CheckSignature(certificate, verifySignatureOnly: true);
        }
        catch (CryptographicException)
        {
            valid = false;
        }

        if (!valid)
        {
            return Mismatch("incoming_xml_signature_invalid");
        }

        if (!ChainOk(certificate, anchors, signedAt))
        {
            return NfeXmlReader.Invalid("incoming_xml_certificate_chain", "O certificado que assinou a nota não é da ICP-Brasil ou não valia na data de emissão.");
        }

        var issuer = inf["emit", NfeXmlReader.NfeNamespace]?["CNPJ", NfeXmlReader.NfeNamespace]?.InnerText;
        var signer = FiscalCertificateInspector.CnpjOf(certificate);
        if (signer is null || issuer is null || !string.Equals(signer, issuer, StringComparison.Ordinal))
        {
            return NfeXmlReader.Invalid("incoming_xml_signer_document", "O certificado que assinou a nota é de outro CNPJ, não do emitente.");
        }

        return new NfeSigner(signer, certificate.GetNameInfo(X509NameType.SimpleName, false));
    }

    /// <summary>Cadeia até uma raiz embutida, sem rede (sem download de AC nem consulta de revogação), na data de emissão da nota.</summary>
    public static bool ChainOk(X509Certificate2 certificate, INfeTrustAnchors anchors, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        ArgumentNullException.ThrowIfNull(anchors);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(anchors.Roots);
        chain.ChainPolicy.ExtraStore.AddRange(anchors.Intermediates);
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = at.UtcDateTime;
        chain.ChainPolicy.VerificationTimeIgnored = false;
        try
        {
            return chain.Build(certificate) && chain.ChainStatus.All(s => s.Status == X509ChainStatusFlags.NoError);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static Error Mismatch(string code) => NfeXmlReader.Invalid(code, IncomingInvoiceRules.SignatureMismatchMessage);

    /// <summary>A referência só resolve para o <c>infNFe</c> único (nunca para outro elemento com o mesmo <c>Id</c>).</summary>
    private sealed class SingleIdSignedXml(XmlDocument document, XmlElement target, string id) : SignedXml(document)
    {
        public override XmlElement? GetIdElement(XmlDocument? document, string idValue) =>
            string.Equals(idValue, id, StringComparison.Ordinal) ? target : null;
    }
}
