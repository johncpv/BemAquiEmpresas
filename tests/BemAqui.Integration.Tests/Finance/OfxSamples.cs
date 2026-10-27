using System.Globalization;
using System.Text;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>Arquivos OFX de exemplo (BNK-03a): 1.x SGML e 2.x XML, gerados na hora com a conta, as datas e os lançamentos pedidos.</summary>
public static class OfxSamples
{
    public sealed record Tx(string? FitId, DateOnly Date, decimal Amount, string Memo, string Type = "OTHER", string? Name = null);

    private static string D(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "120000[-3:BRT]";

    private static string A(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>OFX 1.02 em SGML (folhas sem fechamento), como exportam Itaú, Bradesco e Banco do Brasil.</summary>
    public static byte[] Sgml(string bankId, string accountId, IEnumerable<Tx> transactions, decimal? ledger = null, DateOnly? ledgerDate = null, string? extraHeader = null)
    {
        var list = transactions.ToList();
        var start = list.Count > 0 ? list.Min(t => t.Date) : new DateOnly(2026, 10, 1);
        var end = list.Count > 0 ? list.Max(t => t.Date) : new DateOnly(2026, 10, 1);
        var sb = new StringBuilder();
        sb.Append("OFXHEADER:100\r\nDATA:OFXSGML\r\nVERSION:102\r\nSECURITY:NONE\r\nENCODING:USASCII\r\nCHARSET:1252\r\nCOMPRESSION:NONE\r\nOLDFILEUID:NONE\r\nNEWFILEUID:NONE\r\n\r\n");
        sb.Append(extraHeader);
        sb.Append("<OFX>\r\n<SIGNONMSGSRSV1><SONRS><STATUS><CODE>0<SEVERITY>INFO</STATUS><DTSERVER>20261027<LANGUAGE>POR</SONRS></SIGNONMSGSRSV1>\r\n");
        sb.Append("<BANKMSGSRSV1><STMTTRNRS><TRNUID>1001<STATUS><CODE>0<SEVERITY>INFO</STATUS><STMTRS><CURDEF>BRL\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"<BANKACCTFROM><BANKID>{bankId}<BRANCHID>0001<ACCTID>{accountId}<ACCTTYPE>CHECKING</BANKACCTFROM>\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"<BANKTRANLIST><DTSTART>{D(start)}<DTEND>{D(end)}\r\n");
        foreach (var t in list)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<STMTTRN><TRNTYPE>{t.Type}<DTPOSTED>{D(t.Date)}<TRNAMT>{A(t.Amount)}");
            if (t.FitId is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $"<FITID>{t.FitId}");
            }

            if (t.Name is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $"<NAME>{t.Name}");
            }

            sb.Append(CultureInfo.InvariantCulture, $"<MEMO>{t.Memo}</STMTTRN>\r\n");
        }

        sb.Append("</BANKTRANLIST>\r\n");
        if (ledger is { } l)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<LEDGERBAL><BALAMT>{A(l)}<DTASOF>{D(ledgerDate ?? end)}</LEDGERBAL>\r\n");
        }

        sb.Append("</STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>\r\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    /// <summary>OFX 2.11 em XML (com fechamento em tudo).</summary>
    public static byte[] Xml(string bankId, string accountId, IEnumerable<Tx> transactions, string? doctype = null)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n<?OFX OFXHEADER=\"200\" VERSION=\"211\" SECURITY=\"NONE\" OLDFILEUID=\"NONE\" NEWFILEUID=\"NONE\"?>\n");
        sb.Append(doctype);
        sb.Append("<OFX><BANKMSGSRSV1><STMTTRNRS><TRNUID>1</TRNUID><STMTRS><CURDEF>BRL</CURDEF>");
        sb.Append(CultureInfo.InvariantCulture, $"<BANKACCTFROM><BANKID>{bankId}</BANKID><ACCTID>{accountId}</ACCTID><ACCTTYPE>CHECKING</ACCTTYPE></BANKACCTFROM><BANKTRANLIST>");
        foreach (var t in transactions)
        {
            sb.Append(CultureInfo.InvariantCulture, $"<STMTTRN><TRNTYPE>{t.Type}</TRNTYPE><DTPOSTED>{D(t.Date)}</DTPOSTED><TRNAMT>{A(t.Amount)}</TRNAMT>");
            if (t.FitId is not null)
            {
                sb.Append(CultureInfo.InvariantCulture, $"<FITID>{t.FitId}</FITID>");
            }

            sb.Append(CultureInfo.InvariantCulture, $"<MEMO>{t.Memo}</MEMO></STMTTRN>");
        }

        sb.Append("</BANKTRANLIST><LEDGERBAL><BALAMT>100.00</BALAMT><DTASOF>20261020</DTASOF></LEDGERBAL></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
