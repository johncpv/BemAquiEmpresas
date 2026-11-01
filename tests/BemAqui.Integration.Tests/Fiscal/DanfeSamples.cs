using System.Globalization;
using System.IO.Compression;
using System.Text;
using BemAqui.BuildingBlocks.Pdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>O que muda na DANFE fictícia além da nota: a chave impressa, o destinatário e se os itens somam o total.</summary>
public sealed record DanfeOptions(string? PrintedKey = null, string? Recipient = null, string? PrintedIssuerCnpj = null, decimal? PrintedProducts = null, bool WithKey = true);

/// <summary>
/// DANFE fictícia em PDF (FIS-05), com o leiaute dos quadros da DANFE (rótulo em cima, valor embaixo) gerada pelo QuestPDF a partir da
/// mesma <see cref="NfeSample"/> do XML; e PDFs maliciosos montados à mão (JavaScript, nome com <c>#xx</c>, JavaScript dentro de object
/// stream comprimido, anexo embutido, criptografia), sem texto e com páginas demais.
/// </summary>
public static class DanfeSamples
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static (decimal Products, decimal Icms, decimal Ipi, decimal Total) Totals(NfeSample s)
    {
        decimal products = 0, icms = 0, ipi = 0;
        foreach (var item in s.Items)
        {
            var amount = Math.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero);
            products += amount;
            icms += Math.Round(amount * 0.18m, 2, MidpointRounding.AwayFromZero);
            ipi += Math.Round(amount * item.IpiRate / 100m, 2, MidpointRounding.AwayFromZero);
        }

        return (products, icms, ipi, products + ipi + s.Freight);
    }

    public static async Task<byte[]> PdfAsync(NfeSample s, DanfeOptions? options = null)
    {
        var o = options ?? new DanfeOptions();
        var renderer = new QuestPdfRenderer();
        var key = o.PrintedKey ?? NfeSamples.AccessKey(s);
        var (products, icms, ipi, total) = Totals(s);
        string M(decimal v) => v.ToString("#,##0.00", PtBr);
        var issued = s.IssuedAt.ToOffset(TimeSpan.FromHours(-3)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var cnpj = Format(o.PrintedIssuerCnpj ?? s.IssuerCnpj);
        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20);
            page.DefaultTextStyle(x => x.FontFamily(QuestPdfRenderer.FontFamily).FontSize(7));
            page.Content().Column(col =>
            {
                col.Spacing(6);
                col.Item().Text($"RECEBEMOS DE {s.IssuerName} OS PRODUTOS E/OU SERVIÇOS CONSTANTES DA NOTA FISCAL ELETRÔNICA INDICADA AO LADO").FontSize(6);
                col.Item().Row(r =>
                {
                    r.RelativeItem(3).Element(c => Box(c, "IDENTIFICAÇÃO DO EMITENTE", s.IssuerName));
                    r.RelativeItem(1).Element(c => Box(c, "DANFE", "1 - SAÍDA"));
                    r.RelativeItem(1).Element(c => Box(c, "Nº", s.Number.ToString(CultureInfo.InvariantCulture)));
                });
                if (o.WithKey)
                {
                    col.Item().Element(c => Box(c, "CHAVE DE ACESSO", string.Join(' ', Enumerable.Range(0, 11).Select(i => key.Substring(i * 4, 4)))));
                }

                col.Item().Row(r =>
                {
                    r.RelativeItem(2).Element(c => Box(c, "NATUREZA DA OPERAÇÃO", "VENDA DE MERCADORIA"));
                    r.RelativeItem(1).Element(c => Box(c, "INSCRIÇÃO ESTADUAL", "110042490114"));
                    r.RelativeItem(1).Element(c => Box(c, "CNPJ", cnpj));
                });
                col.Item().Text("DESTINATÁRIO / REMETENTE").Bold();
                col.Item().Row(r =>
                {
                    r.RelativeItem(3).Element(c => Box(c, "NOME / RAZÃO SOCIAL", "KONEBRAS ELEVADORES LTDA"));
                    r.RelativeItem(2).Element(c => Box(c, "CNPJ / CPF", Format(o.Recipient ?? s.RecipientCnpj)));
                    r.RelativeItem(1).Element(c => Box(c, "DATA DA EMISSÃO", issued));
                });
                col.Item().Text("CÁLCULO DO IMPOSTO").Bold();
                col.Item().Row(r =>
                {
                    r.RelativeItem().Element(c => Box(c, "BASE DE CÁLC. DO ICMS", M(products)));
                    r.RelativeItem().Element(c => Box(c, "VALOR DO ICMS", M(icms)));
                    r.RelativeItem().Element(c => Box(c, "BASE DE CÁLC. ICMS S.T.", M(0)));
                    r.RelativeItem().Element(c => Box(c, "VALOR DO ICMS SUBST.", M(0)));
                    r.RelativeItem().Element(c => Box(c, "VALOR TOTAL DOS PRODUTOS", M(o.PrintedProducts ?? products)));
                });
                col.Item().Row(r =>
                {
                    r.RelativeItem().Element(c => Box(c, "VALOR DO FRETE", M(s.Freight)));
                    r.RelativeItem().Element(c => Box(c, "VALOR DO SEGURO", M(0)));
                    r.RelativeItem().Element(c => Box(c, "DESCONTO", M(0)));
                    r.RelativeItem().Element(c => Box(c, "OUTRAS DESPESAS", M(0)));
                    r.RelativeItem().Element(c => Box(c, "VALOR TOTAL DO IPI", M(ipi)));
                    r.RelativeItem().Element(c => Box(c, "VALOR TOTAL DA NOTA", M(total)));
                });
                col.Item().Text("TRANSPORTADOR / VOLUMES TRANSPORTADOS").Bold();
                col.Item().Text("DADOS DOS PRODUTOS / SERVIÇOS").Bold();
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.ConstantColumn(50);
                        cd.RelativeColumn();
                        cd.ConstantColumn(44);
                        cd.ConstantColumn(28);
                        cd.ConstantColumn(28);
                        cd.ConstantColumn(24);
                        cd.ConstantColumn(46);
                        cd.ConstantColumn(56);
                        cd.ConstantColumn(50);
                        cd.ConstantColumn(44);
                        cd.ConstantColumn(44);
                        cd.ConstantColumn(40);
                    });
                    t.Header(h =>
                    {
                        foreach (var label in new[] { "CÓDIGO", "DESCRIÇÃO DO PRODUTO", "NCM/SH", "CST", "CFOP", "UN", "QUANT.", "VALOR UNIT.", "VALOR TOTAL", "BC ICMS", "V. ICMS", "V. IPI" })
                        {
                            h.Cell().Border(0.5f).Padding(3).Text(label).FontSize(5.5f);
                        }
                    });
                    foreach (var item in s.Items)
                    {
                        var amount = Math.Round(item.Quantity * item.UnitPrice, 2, MidpointRounding.AwayFromZero);
                        string[] cells =
                        [
                            item.Code, item.Description, item.Ncm, "000", "5101", item.Unit, item.Quantity.ToString("#,##0.0000", PtBr), item.UnitPrice.ToString("#,##0.0000", PtBr),
                            M(amount), M(amount), M(Math.Round(amount * 0.18m, 2, MidpointRounding.AwayFromZero)), M(Math.Round(amount * item.IpiRate / 100m, 2, MidpointRounding.AwayFromZero)),
                        ];
                        foreach (var cell in cells)
                        {
                            t.Cell().Padding(3).Text(cell).FontSize(6.5f);
                        }
                    }
                });
                col.Item().Text("DADOS ADICIONAIS").Bold();
                col.Item().Text("INFORMAÇÕES COMPLEMENTARES: DOCUMENTO EMITIDO POR ME OU EPP OPTANTE PELO SIMPLES NACIONAL").FontSize(6);
            });
        }));
        return await renderer.RenderAsync(document);
    }

    /// <summary>PDF só com um retângulo: nenhuma letra (escaneado).</summary>
    public static Task<byte[]> ImageOnlyAsync() =>
        new QuestPdfRenderer().RenderAsync(Document.Create(c => c.Page(p =>
        {
            p.Size(PageSizes.A4);
            p.Content().Padding(40).Height(300).Background(Colors.Grey.Lighten2);
        })));

    /// <summary>PDF com texto em 11 páginas (acima do limite de 10).</summary>
    public static Task<byte[]> PagesAsync(int pages) =>
        new QuestPdfRenderer().RenderAsync(Document.Create(c =>
        {
            for (var i = 1; i <= pages; i++)
            {
                var n = i;
                c.Page(p =>
                {
                    p.Size(PageSizes.A4);
                    p.DefaultTextStyle(x => x.FontFamily(QuestPdfRenderer.FontFamily));
                    p.Content().Padding(40).Text($"Página {n} de uma nota fiscal com páginas demais para ler");
                });
            }
        }));

    /// <summary>PDF que abre e roda JavaScript (<c>/OpenAction</c>), com o nome do jeito comum ou escrito com <c>#xx</c>.</summary>
    public static byte[] JavaScript(bool obfuscated = false) =>
        Raw(obfuscated ? "<< /S /J#61vaScript /J#53 (app.alert\\(1\\)) >>" : "<< /S /JavaScript /JS (app.alert\\(1\\)) >>", "/OpenAction 4 0 R");

    /// <summary>PDF com arquivo embutido no catálogo.</summary>
    public static byte[] EmbeddedFile() =>
        Raw("<< /Type /Filespec /F (nota.exe) /EF << /F 5 0 R >> >>", "/Names << /EmbeddedFiles << /Names [(nota.exe) 4 0 R] >> >>", "<< /Type /EmbeddedFile /Length 2 >>\nstream\nMZ\nendstream");

    /// <summary>PDF com o dicionário de criptografia no trailer.</summary>
    public static byte[] Encrypted() => Raw("<< /Filter /Standard /V 1 /R 2 /O (x) /U (y) /P -4 >>", string.Empty, trailerExtra: "/Encrypt 4 0 R");

    /// <summary>
    /// JavaScript escondido num object stream comprimido (PDF 1.5, tabela de referência em stream): os bytes não mostram o nome; só o worker,
    /// depois de abrir, acha a ação.
    /// </summary>
    public static byte[] CompressedJavaScript()
    {
        var offsets = new Dictionary<int, int>();
        var body = new MemoryStream();
        void Write(string text)
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            body.Write(bytes);
        }

        Write("%PDF-1.5\n%âãÏÓ\n");
        offsets[1] = (int)body.Length;
        Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R /OpenAction 4 0 R >>\nendobj\n");
        offsets[2] = (int)body.Length;
        Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        offsets[3] = (int)body.Length;
        Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>\nendobj\n");
        var header = "4 0 ";
        var inner = Encoding.Latin1.GetBytes(header + "<< /S /JavaScript /JS (app.alert\\(1\\)) >>");
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(inner);
        }

        offsets[5] = (int)body.Length;
        Write(string.Create(CultureInfo.InvariantCulture, $"5 0 obj\n<< /Type /ObjStm /N 1 /First {header.Length} /Filter /FlateDecode /Length {packed.Length} >>\nstream\n"));
        body.Write(packed.ToArray());
        Write("\nendstream\nendobj\n");
        offsets[6] = (int)body.Length;
        var xref = new MemoryStream();
        void Entry(byte type, int field2, short field3)
        {
            xref.WriteByte(type);
            xref.Write([(byte)(field2 >> 24), (byte)(field2 >> 16), (byte)(field2 >> 8), (byte)field2]);
            xref.Write([(byte)(field3 >> 8), (byte)field3]);
        }

        Entry(0, 0, -1);
        Entry(1, offsets[1], 0);
        Entry(1, offsets[2], 0);
        Entry(1, offsets[3], 0);
        Entry(2, 5, 0);
        Entry(1, offsets[5], 0);
        Entry(1, offsets[6], 0);
        Write(string.Create(CultureInfo.InvariantCulture, $"6 0 obj\n<< /Type /XRef /Size 7 /W [1 4 2] /Root 1 0 R /Length {xref.Length} >>\nstream\n"));
        body.Write(xref.ToArray());
        Write(string.Create(CultureInfo.InvariantCulture, $"\nendstream\nendobj\nstartxref\n{offsets[6]}\n%%EOF\n"));
        return body.ToArray();
    }

    /// <summary>PDF mínimo montado à mão com o objeto 4 dado (e o 5, se houver) e entradas extras no catálogo e no trailer.</summary>
    private static byte[] Raw(string object4, string catalogExtra, string? object5 = null, string trailerExtra = "")
    {
        var body = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        void Obj(int n, string content)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(body.ToString()));
            body.Append(CultureInfo.InvariantCulture, $"{n} 0 obj\n{content}\nendobj\n");
        }

        Obj(1, $"<< /Type /Catalog /Pages 2 0 R {catalogExtra} >>");
        Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 200] >>");
        Obj(4, object4);
        if (object5 is not null)
        {
            Obj(5, object5);
        }

        var xrefAt = Encoding.Latin1.GetByteCount(body.ToString());
        body.Append(CultureInfo.InvariantCulture, $"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            body.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");
        }

        body.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R {trailerExtra} >>\nstartxref\n{xrefAt}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(body.ToString());
    }

    private static void Box(IContainer container, string label, string value) =>
        container.Border(0.5f).Padding(3).Column(c =>
        {
            c.Item().Text(label).FontSize(5.5f);
            c.Item().Text(value).FontSize(8);
        });

    private static string Format(string cnpj) => cnpj.Length == 14 ? $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}" : cnpj;
}
