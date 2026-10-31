using System.Text;
using System.Xml;
using BemAqui.Fiscal.Application.Incoming;
using BemAqui.Fiscal.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>
/// FIS-04a, unidade: leitor seguro (XXE, "billion laughs", DOCTYPE; CA 1); conferência do XML assinado no próprio teste: válido, alterado
/// em 1 byte, sem protocolo, dois <c>infNFe</c>, duas assinaturas, referência para outro elemento, certificado de outro CNPJ e fora da
/// cadeia (CA 2); a cadeia ICP-Brasil embutida; unidade de estoque e divergências (CA 8 e 10).
/// </summary>
public sealed class IncomingXmlUnitTests
{
    private static ValidatedNfe Valid(NfeSample sample) => NfeXmlValidator.Validate(NfeSamples.Authorized(sample), NfeSamples.Anchors).Value;

    private static string Refused(byte[] content)
    {
        var result = NfeXmlValidator.Validate(content, NfeSamples.Anchors);
        result.IsSuccess.Should().BeFalse();
        return result.Error!.Message;
    }

    [Fact]
    public void Leitor_recusa_DOCTYPE_entidade_externa_billion_laughs_e_xml_malformado()
    {
        foreach (var content in new[] { NfeSamples.Xxe(), NfeSamples.BillionLaughs(), "<nfeProc><aberto>"u8.ToArray(), Encoding.UTF8.GetBytes("<a>" + new string('<', 3) + "</a>") })
        {
            var result = NfeXmlReader.Load(content);
            result.IsSuccess.Should().BeFalse();
            result.Error!.Message.Should().Be(IncomingInvoiceRules.InvalidXmlMessage);
        }

        var deep = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("<a>", 40)) + string.Concat(Enumerable.Repeat("</a>", 40)));
        NfeXmlReader.Load(deep).Error!.Code.Should().Be("incoming_xml_too_deep");
        NfeXmlReader.Load(new byte[IncomingInvoiceRules.MaxBytes + 1]).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Xml_valido_passa_no_esquema_no_protocolo_e_na_assinatura_e_le_os_campos()
    {
        var sample = new NfeSample
        {
            Items = [new("CH-2MM", "CHAPA DE ACO 2MM", "72085100", "UN", 10m, 12.5m, "PC-2026-0007", "1", "L-77", 5m)], Freight = 30m, Complement = "Ref. pedido pc-2026-0008",
            CarrierPerson = true,
        };
        var nfe = Valid(sample);
        nfe.Signer.Document.Should().Be(sample.IssuerCnpj);
        nfe.Nfe.AccessKey.Should().Be(NfeSamples.AccessKey(sample));
        nfe.Nfe.Totals.Should().Be(new IncomingInvoiceTotals(125m, 30m, 0m, 0m, 0m, 6.25m, 125m, 22.5m, 0m, 0m, 161.25m));
        nfe.Nfe.Duplicates.Should().ContainSingle().Which.Amount.Should().Be(161.25m);
        var item = nfe.Nfe.Items.Should().ContainSingle().Subject;
        item.Should().BeEquivalentTo(new { SupplierCode = "CH-2MM", Unit = "UN", Quantity = 10m, UnitPrice = 12.5m, Amount = 125m, LotNumber = "L-77", OrderNumber = "PC-2026-0007", OrderItem = "1" });
        item.Taxes.Ipi.Should().Be(6.25m);
        nfe.Nfe.OrderReferences.Should().BeEquivalentTo(["PC-2026-0007", "PC-2026-0008"]);
        nfe.Nfe.Protocol.Should().Be("135260000000001");

        // O transportador pessoa física não é lido (CA 14): nada dele no que a importação guarda.
        System.Text.Json.JsonSerializer.Serialize(nfe.Nfe).Should().NotContain(NfeSamples.CarrierName).And.NotContain(NfeSamples.CarrierCpf);
    }

    [Fact]
    public void Recusa_alterado_em_1_byte_sem_protocolo_outro_cnpj_fora_da_cadeia_homologacao_e_nao_autorizada()
    {
        var sample = new NfeSample();
        var text = NfeSamples.SignedText(sample);
        Refused(Encoding.UTF8.GetBytes(text.Replace("CHAPA DE ACO 2MM", "CHAPA DE ACO 3MM", StringComparison.Ordinal))).Should().Be(IncomingInvoiceRules.SignatureMismatchMessage);
        Refused(NfeSamples.Authorized(sample, withProtocol: false)).Should().Be(IncomingInvoiceRules.NoProtocolMessage);
        Refused(NfeSamples.Authorized(sample with { ProtocolStatus = 110 })).Should().Be(IncomingInvoiceRules.NoProtocolMessage);
        Refused(NfeSamples.Authorized(sample, NfeSamples.Signer(NfeSamples.NewCnpj()))).Should().Be("O certificado que assinou a nota é de outro CNPJ, não do emitente.");
        Refused(NfeSamples.Authorized(sample, NfeSamples.Signer(sample.IssuerCnpj, trusted: false))).Should().Be("O certificado que assinou a nota não é da ICP-Brasil ou não valia na data de emissão.");
        Refused(NfeSamples.Authorized(sample with { Environment = 2 })).Should().Contain("homologação");

        // Chave do protocolo diferente da nota.
        Refused(Encoding.UTF8.GetBytes(text.Replace("<chNFe>" + NfeSamples.AccessKey(sample), "<chNFe>" + NfeSamples.AccessKey(sample with { Number = sample.Number + 1 }), StringComparison.Ordinal)))
            .Should().Contain("protocolo de autorização não é desta nota");
    }

    [Fact]
    public void Assinatura_exige_um_infNFe_uma_Signature_e_a_referencia_pelo_Id()
    {
        var sample = new NfeSample();
        XmlDocument Load(string text)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(text);
            return doc;
        }

        string Check(XmlDocument doc) => NfeSignatureValidator.Validate(doc, NfeSamples.Anchors, sample.IssuedAt) is { IsSuccess: false } r ? r.Error!.Message : "ok";
        var ns = new XmlNamespaceManager(new NameTable());
        ns.AddNamespace("n", NfeXmlReader.NfeNamespace);
        ns.AddNamespace("ds", NfeXmlReader.DsigNamespace);

        Check(Load(NfeSamples.SignedText(sample))).Should().Be("ok");

        var twoInf = Load(NfeSamples.SignedText(sample));
        var nfe = (XmlElement)twoInf.SelectSingleNode("//n:NFe", ns)!;
        nfe.InsertBefore(nfe.SelectSingleNode("n:infNFe", ns)!.CloneNode(true), nfe.FirstChild);
        Check(twoInf).Should().Be(IncomingInvoiceRules.SignatureMismatchMessage);

        var twoSignatures = Load(NfeSamples.SignedText(sample));
        var signature = twoSignatures.SelectSingleNode("//ds:Signature", ns)!;
        signature.ParentNode!.AppendChild(signature.CloneNode(true));
        Check(twoSignatures).Should().Be(IncomingInvoiceRules.SignatureMismatchMessage);

        // Assinatura válida de outro elemento (o infProt), colocada na nota: a referência não é o Id do infNFe.
        Refused(NfeSamples.Authorized(sample, referenceId: "ID135260000000001")).Should().Be(IncomingInvoiceRules.SignatureMismatchMessage);
        Check(Load(NfeSamples.SignedText(sample, referenceId: "ID135260000000001"))).Should().Be(IncomingInvoiceRules.SignatureMismatchMessage);
    }

    [Fact]
    public void Cadeia_ICP_Brasil_embutida_tem_as_raizes_e_nao_confia_na_raiz_de_teste()
    {
        var anchors = new IcpBrasilTrustAnchors();
        anchors.Roots.Count.Should().BeGreaterThanOrEqualTo(6);
        anchors.Roots.Cast<System.Security.Cryptography.X509Certificates.X509Certificate2>().Should().Contain(c => c.Subject.Contains("Raiz Brasileira v5", StringComparison.Ordinal));
        anchors.Intermediates.Count.Should().BeGreaterThan(100);
        NfeSignatureValidator.ChainOk(NfeSamples.Signer(NfeSamples.NewCnpj()), anchors, DateTimeOffset.UtcNow).Should().BeFalse();
        NfeSchema.Schemas.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Unidade_de_estoque_com_4_casas_e_divergencias_acima_critica_abaixo_alerta()
    {
        IncomingInvoiceRules.StockQuantity(3m, 12m).Should().Be(36m);
        IncomingInvoiceRules.StockQuantity(1.3333m, 0.333333m).Should().Be(0.4444m);
        IncomingInvoiceRules.StockUnitPrice(120m, 12m).Should().Be(10m);

        var above = IncomingInvoiceRules.Compare(openQuantity: 30m, billedQuantity: 36m, orderPrice: 10m, invoicePrice: 10.5m);
        above.QuantityDifference.Should().Be(6m);
        above.QuantityLevel.Should().Be(DivergenceLevel.Critical);
        above.PriceDifference.Should().Be(0.5m);
        above.PricePercent.Should().Be(5m);
        above.ValueDifference.Should().Be(18m);
        above.PriceLevel.Should().Be(DivergenceLevel.Critical);

        var below = IncomingInvoiceRules.Compare(40m, 36m, 10m, 9m);
        below.QuantityLevel.Should().Be(DivergenceLevel.Warning);
        below.PriceLevel.Should().Be(DivergenceLevel.Warning);
        IncomingInvoiceRules.Compare(36m, 36m, 10m, 10m).Should().Match<ItemComparison>(c => c.QuantityLevel == DivergenceLevel.Ok && c.PriceLevel == DivergenceLevel.Ok);
        IncomingInvoiceRules.CompareTotal(50m, 30m).Level.Should().Be(DivergenceLevel.Warning);

        var item = new IncomingInvoiceItem { Quantity = 3m, UnitPrice = 120m, Unit = "CX" };
        item.MapTo(Guid.NewGuid(), null, ItemMappingSource.Manual);
        item.IsResolved.Should().BeFalse("sem fator, o campo é obrigatório antes de conferir (CA 8)");
        item.SetFactor(12m);
        (item.StockQuantity, item.StockUnitPrice, item.IsResolved).Should().Be((36m, 10m, true));
        IncomingInvoiceRules.MaskKey("35261047225005000186550010000012341123456785").Should().Be("…456785");
        IncomingInvoiceRules.IsValidAccessKey(NfeSamples.AccessKey(new NfeSample())).Should().BeTrue();
    }
}
