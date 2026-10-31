using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using BemAqui.Fiscal.Application.Incoming;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>Item da nota fictícia.</summary>
public sealed record NfeSampleItem(string Code, string Description, string Ncm, string Unit, decimal Quantity, decimal UnitPrice, string? OrderNumber = null, string? OrderItem = null, string? Lot = null, decimal IpiRate = 0m);

/// <summary>Nota fictícia (FIS-04a): tudo o que o teste muda para montar o XML; os valores padrão formam uma NF-e 4.00 válida no esquema.</summary>
public sealed record NfeSample
{
    public string IssuerCnpj { get; init; } = NfeSamples.NewCnpj();

    public string IssuerName { get; init; } = "FORNECEDOR TESTE LTDA";

    public string RecipientCnpj { get; init; } = NfeSamples.TenantCnpj;

    public long Number { get; init; } = Random.Shared.Next(1, 999_999_999);

    public int Series { get; init; } = 1;

    public DateTimeOffset IssuedAt { get; init; } = new DateTimeOffset(DateTime.UtcNow.Date.AddDays(-1).AddHours(13), TimeSpan.Zero);

    public IReadOnlyList<NfeSampleItem> Items { get; init; } = [new("CH-2MM", "CHAPA DE ACO 2MM", "72085100", "UN", 10m, 12.5m)];

    public decimal Freight { get; init; }

    public string? OrderNumber { get; init; }

    public string? Complement { get; init; }

    public int ProtocolStatus { get; init; } = 100;

    public int Environment { get; init; } = 1;

    /// <summary>Transportador pessoa física (CA 14): o nome e o CPF não podem ir para o log nem para a auditoria.</summary>
    public bool CarrierPerson { get; init; }

    public string Cnf { get; init; } = Random.Shared.Next(10_000_000, 99_999_999).ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// NF-e de entrada fictícia, assinada no próprio teste (nunca há certificado no repositório): uma AC raiz de teste no lugar da ICP-Brasil
/// (as âncoras do fixture) e o certificado do emitente com o CNPJ no <c>otherName</c> 2.16.76.1.3.3, como o e-CNPJ.
/// </summary>
public static class NfeSamples
{
    public const string TenantCnpj = "47225005000186";
    public const string CarrierName = "JOAO CANARIO TRANSPORTADOR";
    public const string CarrierCpf = "52998224725";

    private static readonly Lazy<(X509Certificate2 Root, RSA Key)> RootCa = new(CreateRoot);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, X509Certificate2> Leaves = new();

    public static INfeTrustAnchors Anchors { get; } = new TestAnchors();

    public static X509Certificate2 Root => RootCa.Value.Root;

    /// <summary>CNPJ válido aleatório (dígitos verificadores calculados).</summary>
    public static string NewCnpj()
    {
        var digits = Enumerable.Range(0, 8).Select(_ => Random.Shared.Next(0, 10)).Concat([0, 0, 0, 1]).ToList();
        digits.Add(Dv(digits, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        digits.Add(Dv(digits, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(digits);
    }

    /// <summary>Certificado do emitente assinado pela raiz de teste (ou autoassinado, fora da cadeia).</summary>
    public static X509Certificate2 Signer(string cnpj, bool trusted = true) =>
        trusted ? Leaves.GetOrAdd(cnpj, c => CreateLeaf(c, RootCa.Value.Key, Root.SubjectName)) : CreateLeaf(cnpj, null, null);

    public static string AccessKey(NfeSample s)
    {
        var aamm = s.IssuedAt.ToOffset(TimeSpan.FromHours(-3)).ToString("yyMM", CultureInfo.InvariantCulture);
        var body = "35" + aamm + s.IssuerCnpj + "55" + s.Series.ToString("000", CultureInfo.InvariantCulture) + s.Number.ToString("000000000", CultureInfo.InvariantCulture) + "1" + s.Cnf;
        var sum = 0;
        var weight = 2;
        for (var i = body.Length - 1; i >= 0; i--)
        {
            sum += (body[i] - '0') * weight;
            weight = weight == 9 ? 2 : weight + 1;
        }

        var rest = sum % 11;
        return body + (rest < 2 ? 0 : 11 - rest).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>O <c>nfeProc</c> sem assinatura (o lugar da <c>Signature</c> fica vazio).</summary>
    public static string Draft(NfeSample s, bool withProtocol = true)
    {
        var key = AccessKey(s);
        string D(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        string Q(decimal v) => v.ToString("0.0000", CultureInfo.InvariantCulture);
        string P(decimal v) => v.ToString("0.0000######", CultureInfo.InvariantCulture);
        var at = s.IssuedAt.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        var det = new StringBuilder();
        decimal products = 0, icms = 0, ipi = 0, pis = 0, cofins = 0;
        var n = 0;
        foreach (var item in s.Items)
        {
            n++;
            var amount = Math.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero);
            var vIcms = Math.Round(amount * 0.18m, 2, MidpointRounding.AwayFromZero);
            var vIpi = Math.Round(amount * item.IpiRate / 100m, 2, MidpointRounding.AwayFromZero);
            var vPis = Math.Round(amount * 0.0165m, 2, MidpointRounding.AwayFromZero);
            var vCofins = Math.Round(amount * 0.076m, 2, MidpointRounding.AwayFromZero);
            products += amount;
            icms += vIcms;
            ipi += vIpi;
            pis += vPis;
            cofins += vCofins;
            det.Append(CultureInfo.InvariantCulture, $"<det nItem=\"{n}\"><prod><cProd>{item.Code}</cProd><cEAN>SEM GTIN</cEAN><xProd>{item.Description}</xProd><NCM>{item.Ncm}</NCM><CFOP>5101</CFOP>")
                .Append(CultureInfo.InvariantCulture, $"<uCom>{item.Unit}</uCom><qCom>{Q(item.Quantity)}</qCom><vUnCom>{P(item.UnitPrice)}</vUnCom><vProd>{D(amount)}</vProd><cEANTrib>SEM GTIN</cEANTrib>")
                .Append(CultureInfo.InvariantCulture, $"<uTrib>{item.Unit}</uTrib><qTrib>{Q(item.Quantity)}</qTrib><vUnTrib>{P(item.UnitPrice)}</vUnTrib><indTot>1</indTot>")
                .Append(item.OrderNumber is null ? string.Empty : $"<xPed>{item.OrderNumber}</xPed>")
                .Append(item.OrderItem is null ? string.Empty : $"<nItemPed>{item.OrderItem}</nItemPed>")
                .Append(item.Lot is null ? string.Empty : $"<rastro><nLote>{item.Lot}</nLote><qLote>{item.Quantity.ToString("0.000", CultureInfo.InvariantCulture)}</qLote><dFab>2026-09-01</dFab><dVal>2028-09-01</dVal></rastro>")
                .Append(CultureInfo.InvariantCulture, $"</prod><imposto><ICMS><ICMS00><orig>0</orig><CST>00</CST><modBC>3</modBC><vBC>{D(amount)}</vBC><pICMS>18.00</pICMS><vICMS>{D(vIcms)}</vICMS></ICMS00></ICMS>")
                .Append(CultureInfo.InvariantCulture, $"<IPI><cEnq>999</cEnq><IPITrib><CST>50</CST><vBC>{D(amount)}</vBC><pIPI>{D(item.IpiRate)}</pIPI><vIPI>{D(vIpi)}</vIPI></IPITrib></IPI>")
                .Append(CultureInfo.InvariantCulture, $"<PIS><PISAliq><CST>01</CST><vBC>{D(amount)}</vBC><pPIS>1.65</pPIS><vPIS>{D(vPis)}</vPIS></PISAliq></PIS>")
                .Append(CultureInfo.InvariantCulture, $"<COFINS><COFINSAliq><CST>01</CST><vBC>{D(amount)}</vBC><pCOFINS>7.60</pCOFINS><vCOFINS>{D(vCofins)}</vCOFINS></COFINSAliq></COFINS></imposto></det>");
        }

        var total = products + ipi + s.Freight;
        var carrier = s.CarrierPerson ? $"<transporta><CPF>{CarrierCpf}</CPF><xNome>{CarrierName}</xNome><xEnder>RUA DO CANARIO 1</xEnder><xMun>SAO PAULO</xMun><UF>SP</UF></transporta>" : string.Empty;
        var nfe = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"<NFe xmlns=\"{NfeXmlReader.NfeNamespace}\"><infNFe versao=\"4.00\" Id=\"NFe{key}\">")
            .Append(CultureInfo.InvariantCulture, $"<ide><cUF>35</cUF><cNF>{s.Cnf}</cNF><natOp>VENDA DE MERCADORIA</natOp><mod>55</mod><serie>{s.Series}</serie><nNF>{s.Number}</nNF><dhEmi>{at}</dhEmi>")
            .Append(CultureInfo.InvariantCulture, $"<tpNF>1</tpNF><idDest>1</idDest><cMunFG>3550308</cMunFG><tpImp>1</tpImp><tpEmis>1</tpEmis><cDV>{key[^1]}</cDV><tpAmb>{s.Environment}</tpAmb><finNFe>1</finNFe>")
            .Append("<indFinal>0</indFinal><indPres>0</indPres><procEmi>0</procEmi><verProc>TESTE 1.0</verProc></ide>")
            .Append(CultureInfo.InvariantCulture, $"<emit><CNPJ>{s.IssuerCnpj}</CNPJ><xNome>{s.IssuerName}</xNome><xFant>FORNECEDOR</xFant><enderEmit><xLgr>RUA DAS INDUSTRIAS</xLgr><nro>100</nro>")
            .Append("<xBairro>DISTRITO INDUSTRIAL</xBairro><cMun>3550308</cMun><xMun>SAO PAULO</xMun><UF>SP</UF><CEP>01001000</CEP><cPais>1058</cPais><xPais>BRASIL</xPais></enderEmit><IE>110042490114</IE><CRT>3</CRT></emit>")
            .Append(CultureInfo.InvariantCulture, $"<dest><CNPJ>{s.RecipientCnpj}</CNPJ><xNome>KONEBRAS ELEVADORES LTDA</xNome><enderDest><xLgr>AVENIDA BRASIL</xLgr><nro>200</nro><xBairro>CENTRO</xBairro>")
            .Append("<cMun>3550308</cMun><xMun>SAO PAULO</xMun><UF>SP</UF><CEP>01002000</CEP><cPais>1058</cPais><xPais>BRASIL</xPais></enderDest><indIEDest>1</indIEDest><IE>110042490115</IE></dest>")
            .Append(det)
            .Append(CultureInfo.InvariantCulture, $"<total><ICMSTot><vBC>{D(products)}</vBC><vICMS>{D(icms)}</vICMS><vICMSDeson>0.00</vICMSDeson><vFCP>0.00</vFCP><vBCST>0.00</vBCST><vST>0.00</vST>")
            .Append(CultureInfo.InvariantCulture, $"<vFCPST>0.00</vFCPST><vFCPSTRet>0.00</vFCPSTRet><vProd>{D(products)}</vProd><vFrete>{D(s.Freight)}</vFrete><vSeg>0.00</vSeg><vDesc>0.00</vDesc><vII>0.00</vII>")
            .Append(CultureInfo.InvariantCulture, $"<vIPI>{D(ipi)}</vIPI><vIPIDevol>0.00</vIPIDevol><vPIS>{D(pis)}</vPIS><vCOFINS>{D(cofins)}</vCOFINS><vOutro>0.00</vOutro><vNF>{D(total)}</vNF></ICMSTot></total>")
            .Append(CultureInfo.InvariantCulture, $"<transp><modFrete>{(s.Freight > 0 ? 0 : 9)}</modFrete>{carrier}</transp>")
            .Append(CultureInfo.InvariantCulture, $"<cobr><fat><nFat>{s.Number}</nFat><vOrig>{D(total)}</vOrig><vDesc>0.00</vDesc><vLiq>{D(total)}</vLiq></fat>")
            .Append(CultureInfo.InvariantCulture, $"<dup><nDup>001</nDup><dVenc>{s.IssuedAt.AddDays(30):yyyy-MM-dd}</dVenc><vDup>{D(total)}</vDup></dup></cobr>")
            .Append(CultureInfo.InvariantCulture, $"<pag><detPag><tPag>15</tPag><vPag>{D(total)}</vPag></detPag></pag>")
            .Append(s.Complement is null ? string.Empty : $"<infAdic><infCpl>{s.Complement}</infCpl></infAdic>")
            .Append(s.OrderNumber is null ? string.Empty : $"<compra><xPed>{s.OrderNumber}</xPed></compra>")
            .Append("</infNFe></NFe>");
        if (!withProtocol)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + nfe;
        }

        var received = s.IssuedAt.AddMinutes(1).ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        return $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><nfeProc xmlns=\"{NfeXmlReader.NfeNamespace}\" versao=\"4.00\">{nfe}"
            + $"<protNFe versao=\"4.00\"><infProt Id=\"ID135260000000001\"><tpAmb>{s.Environment}</tpAmb><verAplic>SP_NFE_PL009_V4</verAplic><chNFe>{key}</chNFe><dhRecbto>{received}</dhRecbto>"
            + $"<nProt>135260000000001</nProt><cStat>{s.ProtocolStatus}</cStat><xMotivo>Autorizado o uso da NF-e</xMotivo></infProt></protNFe></nfeProc>";
    }

    /// <summary>XML assinado (RSA-SHA1, enveloped + C14N, como a NF-e) pelo certificado dado (padrão: o do emitente, na cadeia de teste).</summary>
    public static byte[] Authorized(NfeSample s, X509Certificate2? signer = null, bool withProtocol = true, string? referenceId = null) =>
        Encoding.UTF8.GetBytes(SignedText(s, signer, withProtocol, referenceId));

    public static string SignedText(NfeSample s, X509Certificate2? signer = null, bool withProtocol = true, string? referenceId = null)
    {
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(Draft(s, withProtocol));
        var ns = new XmlNamespaceManager(document.NameTable);
        ns.AddNamespace("n", NfeXmlReader.NfeNamespace);
        var nfe = (XmlElement)document.SelectSingleNode("//n:NFe", ns)!;
        var signature = Sign(document, signer ?? Signer(s.IssuerCnpj), referenceId ?? "NFe" + AccessKey(s));
        var inf = nfe.SelectSingleNode("n:infNFe", ns)!;
        nfe.InsertAfter(document.ImportNode(signature, true), inf);
        return document.OuterXml;
    }

    /// <summary>Assinatura do elemento com o <c>Id</c> dado (o <c>infNFe</c> ou, no ataque, outro elemento).</summary>
    public static XmlElement Sign(XmlDocument document, X509Certificate2 signer, string id)
    {
        var signed = new IdSignedXml(document) { SigningKey = signer.GetRSAPrivateKey() };
        signed.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        signed.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;
        var reference = new Reference("#" + id) { DigestMethod = SignedXml.XmlDsigSHA1Url };
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigC14NTransform());
        signed.AddReference(reference);
        var info = new KeyInfo();
        info.AddClause(new KeyInfoX509Data(signer));
        signed.KeyInfo = info;
        signed.ComputeSignature();
        return signed.GetXml();
    }

    /// <summary>XML com DOCTYPE e entidade externa (XXE).</summary>
    public static byte[] Xxe() => Encoding.UTF8.GetBytes(
        $"<?xml version=\"1.0\"?><!DOCTYPE nfeProc [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><nfeProc xmlns=\"{NfeXmlReader.NfeNamespace}\" versao=\"4.00\"><x>&xxe;</x></nfeProc>");

    /// <summary>"Billion laughs": entidades aninhadas.</summary>
    public static byte[] BillionLaughs()
    {
        var sb = new StringBuilder("<?xml version=\"1.0\"?><!DOCTYPE lolz [<!ENTITY lol \"lol\">");
        for (var i = 1; i <= 9; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<!ENTITY lol{i} \"{string.Concat(Enumerable.Repeat($"&lol{(i == 1 ? string.Empty : (i - 1).ToString(CultureInfo.InvariantCulture))};", 10))}\">");
        }

        return Encoding.UTF8.GetBytes(sb.Append("]><lolz>&lol9;</lolz>").ToString());
    }

    private static int Dv(List<int> digits, int[] weights)
    {
        var rest = digits.Select((d, i) => d * weights[i]).Sum() % 11;
        return rest < 2 ? 0 : 11 - rest;
    }

    private static (X509Certificate2, RSA) CreateRoot()
    {
        var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=AC Raiz de Teste BemAqui, O=ICP-Brasil Teste, C=BR", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddYears(5));
        return (root, key);
    }

    private static X509Certificate2 CreateLeaf(string cnpj, RSA? issuerKey, X500DistinguishedName? issuer)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN=FORNECEDOR TESTE LTDA:{cnpj}, O=ICP-Brasil Teste, C=BR", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation, true));
        request.CertificateExtensions.Add(new X509Extension("2.5.29.17", AltName(cnpj), false));
        var notBefore = DateTimeOffset.UtcNow.AddDays(-60);
        var notAfter = DateTimeOffset.UtcNow.AddYears(1);
        if (issuerKey is null || issuer is null)
        {
            return request.CreateSelfSigned(notBefore, notAfter);
        }

        var serial = RandomNumberGenerator.GetBytes(12);
        serial[0] &= 0x7F;
        var generator = X509SignatureGenerator.CreateForRSA(issuerKey, RSASignaturePadding.Pkcs1);
        using var cert = request.Create(issuer, generator, notBefore, notAfter, serial);
        return cert.CopyWithPrivateKey(key);
    }

    private static byte[] AltName(string cnpj)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            var otherName = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            using (writer.PushSequence(otherName))
            {
                writer.WriteObjectIdentifier("2.16.76.1.3.3");
                using (writer.PushSequence(otherName))
                {
                    writer.WriteOctetString(Encoding.ASCII.GetBytes(cnpj));
                }
            }
        }

        return writer.Encode();
    }

    /// <summary>Resolve o <c>Id</c> de qualquer elemento (o assinador do teste; o validador de produção só aceita o <c>infNFe</c>).</summary>
    private sealed class IdSignedXml(XmlDocument document) : SignedXml(document)
    {
        public override XmlElement? GetIdElement(XmlDocument? document, string idValue) =>
            document?.SelectSingleNode($"//*[@Id='{idValue}']") as XmlElement ?? base.GetIdElement(document, idValue);
    }

    private sealed class TestAnchors : INfeTrustAnchors
    {
        public X509Certificate2Collection Roots => [Root];

        public X509Certificate2Collection Intermediates => [];
    }
}
