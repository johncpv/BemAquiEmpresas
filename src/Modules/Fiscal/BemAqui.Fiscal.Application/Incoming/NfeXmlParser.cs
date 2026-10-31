using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;

namespace BemAqui.Fiscal.Application.Incoming;

/// <summary>Item lido do XML (<c>det</c>).</summary>
public sealed record ParsedNfeItem(
    int Sequence, string SupplierCode, string Description, string? Ncm, string? Cfop, string Unit, decimal Quantity, decimal UnitPrice, decimal Amount, decimal Discount,
    IncomingItemTaxes Taxes, string? LotNumber, string? OrderNumber, string? OrderItem);

/// <summary>
/// O que a importação guarda da NF-e (CA 5): emitente (pessoa jurídica), destinatário (só o CNPJ, para conferir), número, série, chave,
/// emissão, totais, duplicatas, itens e o protocolo. O transportador e qualquer outra pessoa citada na nota não são lidos (CA 14).
/// </summary>
public sealed record ParsedNfe(
    string AccessKey, int Model, int Environment, long Number, int Series, DateTimeOffset IssuedAt,
    string? IssuerCnpj, string IssuerName, string? IssuerTradeName, string? IssuerStateRegistration, IncomingIssuerAddress IssuerAddress,
    string? RecipientCnpj, IncomingInvoiceTotals Totals, IReadOnlyList<IncomingDuplicate> Duplicates, IReadOnlyList<ParsedNfeItem> Items,
    IReadOnlyList<string> OrderReferences, string? ProtocolKey, string? Protocol, int? ProtocolStatus, int? ProtocolEnvironment, DateTimeOffset? AuthorizedAt);

/// <summary>Leitura dos campos da NF-e 4.00 já validada pelo esquema (os números vêm com ponto, cultura invariante).</summary>
public static partial class NfeXmlParser
{
    public static Result<ParsedNfe> Parse(XmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var ns = new XmlNamespaceManager(document.NameTable);
        ns.AddNamespace("n", NfeXmlReader.NfeNamespace);
        var inf = document.SelectSingleNode("/n:nfeProc/n:NFe/n:infNFe", ns) as XmlElement;
        if (inf is null)
        {
            return NfeXmlReader.Invalid("incoming_xml_invalid");
        }

        var id = inf.GetAttribute("Id");
        var key = id.StartsWith("NFe", StringComparison.Ordinal) ? id[3..] : string.Empty;
        string? Text(XmlNode? node, string path) => node?.SelectSingleNode(path, ns)?.InnerText is { } t && t.Trim().Length > 0 ? t.Trim() : null;
        decimal Dec(XmlNode? node, string path) => Text(node, path) is { } t ? decimal.Parse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : 0m;
        int Int(XmlNode? node, string path) => Text(node, path) is { } t ? int.Parse(t, CultureInfo.InvariantCulture) : 0;

        var ide = inf.SelectSingleNode("n:ide", ns);
        var emit = inf.SelectSingleNode("n:emit", ns);
        var address = emit?.SelectSingleNode("n:enderEmit", ns);
        var tot = inf.SelectSingleNode("n:total/n:ICMSTot", ns);
        var totals = new IncomingInvoiceTotals(
            Dec(tot, "n:vProd"), Dec(tot, "n:vFrete"), Dec(tot, "n:vSeg"), Dec(tot, "n:vDesc"), Dec(tot, "n:vOutro"), Dec(tot, "n:vIPI"),
            Dec(tot, "n:vBC"), Dec(tot, "n:vICMS"), Dec(tot, "n:vBCST"), Dec(tot, "n:vST"), Dec(tot, "n:vNF"));
        var duplicates = inf.SelectNodes("n:cobr/n:dup", ns)!.Cast<XmlNode>()
            .Select(d => new IncomingDuplicate(Text(d, "n:nDup") ?? string.Empty, Text(d, "n:dVenc") is { } v ? DateOnly.ParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null, Dec(d, "n:vDup")))
            .ToList();

        var items = new List<ParsedNfeItem>();
        foreach (XmlElement det in inf.SelectNodes("n:det", ns)!)
        {
            var prod = det.SelectSingleNode("n:prod", ns);
            var tax = det.SelectSingleNode("n:imposto", ns);
            var icms = tax?.SelectSingleNode("n:ICMS/*", ns);
            var ipi = tax?.SelectSingleNode("n:IPI/n:IPITrib", ns);
            var taxes = new IncomingItemTaxes(
                Text(icms, "n:CST") ?? Text(icms, "n:CSOSN"), Dec(icms, "n:vBC"), Dec(icms, "n:pICMS"), Dec(icms, "n:vICMS"), Dec(icms, "n:vBCST"), Dec(icms, "n:vICMSST"),
                Dec(ipi, "n:pIPI"), Dec(ipi, "n:vIPI"), Dec(tax, "n:PIS/*/n:vPIS"), Dec(tax, "n:COFINS/*/n:vCOFINS"));
            items.Add(new ParsedNfeItem(
                int.Parse(det.GetAttribute("nItem"), CultureInfo.InvariantCulture), Text(prod, "n:cProd") ?? string.Empty, Text(prod, "n:xProd") ?? string.Empty,
                Text(prod, "n:NCM"), Text(prod, "n:CFOP"), (Text(prod, "n:uCom") ?? string.Empty).ToUpperInvariant(), Dec(prod, "n:qCom"), Dec(prod, "n:vUnCom"),
                Dec(prod, "n:vProd"), Dec(prod, "n:vDesc"), taxes, Text(prod, "n:rastro/n:nLote"), Text(prod, "n:xPed"), Text(prod, "n:nItemPed")));
        }

        var references = new List<string>();
        references.AddRange(items.Select(i => i.OrderNumber).OfType<string>());
        if (Text(inf, "n:compra/n:xPed") is { } purchase)
        {
            references.Add(purchase);
        }

        if (Text(inf, "n:infAdic/n:infCpl") is { } complement)
        {
            references.AddRange(OrderNumberPattern().Matches(complement).Select(m => m.Value.ToUpperInvariant()));
        }

        var prot = document.SelectSingleNode("/n:nfeProc/n:protNFe/n:infProt", ns);
        return new ParsedNfe(
            key, Int(ide, "n:mod"), Int(ide, "n:tpAmb"), long.Parse(Text(ide, "n:nNF") ?? "0", CultureInfo.InvariantCulture), Int(ide, "n:serie"),
            DateTimeOffset.Parse(Text(ide, "n:dhEmi") ?? string.Empty, CultureInfo.InvariantCulture),
            Text(emit, "n:CNPJ"), Trim(Text(emit, "n:xNome"), 200) ?? string.Empty, Trim(Text(emit, "n:xFant"), 200), Text(emit, "n:IE"),
            new IncomingIssuerAddress(Trim(Text(address, "n:xLgr"), 200), Trim(Text(address, "n:nro"), 60), Trim(Text(address, "n:xCpl"), 100), Trim(Text(address, "n:xBairro"), 100),
                Trim(Text(address, "n:xMun"), 100), Text(address, "n:cMun"), Text(address, "n:UF"), Text(address, "n:CEP")),
            Text(inf, "n:dest/n:CNPJ"), totals, duplicates, items,
            references.Select(r => r.Trim()).Where(r => r.Length is > 0 and <= 60).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList(),
            Text(prot, "n:chNFe"), Text(prot, "n:nProt"), Text(prot, "n:cStat") is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : null,
            Text(prot, "n:tpAmb") is { } a ? int.Parse(a, CultureInfo.InvariantCulture) : null,
            Text(prot, "n:dhRecbto") is { } r ? DateTimeOffset.Parse(r, CultureInfo.InvariantCulture) : null);
    }

    private static string? Trim(string? text, int max) => text is null ? null : text.Length <= max ? text : text[..max];

    /// <summary>Número do pedido de compra do BemAqui (<c>PC-AAAA-0001</c>) citado na informação complementar.</summary>
    [GeneratedRegex(@"\bPC-\d{4}-\d{4,6}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex OrderNumberPattern();
}
