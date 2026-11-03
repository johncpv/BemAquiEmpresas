using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BemAqui.BuildingBlocks.Pdf;

/// <summary>Empresa no cabeçalho (de <c>ICompanyIdentity</c>, pelo <see cref="IPdfCompanySource"/>): logo só <c>Clean</c>.</summary>
public sealed record PdfCompany(string Name, string LegalName, string? Document, string? Address, string? Contact, byte[]? Logo, bool Complete);

/// <summary>Quem emite e quando (rodapé), mais a empresa: montado pelo <see cref="PdfFrameFactory"/> a cada geração.</summary>
public sealed record PdfFrame(PdfCompany Company, string IssuedBy, DateTimeOffset IssuedAt);

/// <summary>
/// Faixa de título: tipo do documento, número, data, marca e orientação. <paramref name="Notice"/> é o aviso da situação (EXP-03: "Pedido não
/// aprovado: não envie ao fornecedor.", "Validade vencida em …"): vai na faixa junto da marca, ou sozinho (faixa âmbar, sem marca d'água).
/// </summary>
public sealed record PdfDocumentHeader(string Title, string? Number = null, DateOnly? Date = null, PdfMark Mark = PdfMark.None, bool Landscape = false, string? Notice = null);

/// <summary>
/// Modelo único de PDF (ADR 0045 §1): A4 (paisagem quando pedido), margem 32, Archivo com dígitos tabulares, cabeçalho com logo, razão
/// social e CNPJ, faixa de título com número e data, marca padrão (faixa + marca d'água em todas as páginas) e rodapé "Emitido por {usuário}
/// em {data hora} · Página x de y". O documento só escreve <see cref="ComposeContent"/>; cores e medidas vêm de <see cref="PdfTheme"/>.
/// Documento novo nasce aqui; os 12 layouts antigos migram quando a story tocar neles.
/// </summary>
public abstract class PdfDocumentFrame(PdfFrame frame, PdfDocumentHeader header) : IDocument
{
    protected PdfFrame Frame { get; } = frame ?? throw new ArgumentNullException(nameof(frame));

    protected PdfDocumentHeader Heading { get; } = header ?? throw new ArgumentNullException(nameof(header));

    public DocumentMetadata GetMetadata() => new()
    {
        Title = Heading.Number is { } n ? $"{Heading.Title} {n}" : Heading.Title,
        Author = Frame.Company.Name,
        Creator = "BemAqui Empresas",
    };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(Heading.Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
        page.Margin(PdfTheme.Margin);
        page.DefaultTextStyle(t => t.FontFamily(QuestPdfRenderer.FontFamily).FontSize(PdfTheme.FontSize).FontColor(PdfTheme.Ink).EnableFontFeature(FontFeatures.TabularFigures));
        PdfMarks.Watermark(page, Heading.Mark);
        page.Header().Element(ComposeHeader);
        page.Content().PaddingVertical(10).Element(ComposeContent);
        page.Footer().Element(ComposeFooter);
    });

    /// <summary>O corpo do documento, entre a faixa de título e o rodapé.</summary>
    protected abstract void ComposeContent(IContainer container);

    private void ComposeHeader(IContainer c) => c.Column(col =>
    {
        col.Item().Row(row =>
        {
            var company = Frame.Company;
            if (company.Logo is { Length: > 0 } logo)
            {
                row.ConstantItem(110).Height(44).Image(logo).FitArea();
            }
            else
            {
                row.ConstantItem(180).Text(company.Name).FontSize(13).SemiBold();
            }

            row.RelativeItem().AlignRight().Column(info =>
            {
                info.Item().AlignRight().Text(company.LegalName).SemiBold();
                if (company.Document is { Length: > 0 } doc)
                {
                    info.Item().AlignRight().Text("CNPJ " + doc);
                }

                if (company.Address is { Length: > 0 } address)
                {
                    info.Item().AlignRight().Text(address).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                }

                if (company.Contact is { Length: > 0 } contact)
                {
                    info.Item().AlignRight().Text(contact).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                }
            });
        });
        col.Item().PaddingTop(8).BorderBottom(2).BorderColor(PdfTheme.Accent).PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().Text(t =>
            {
                t.Span(Heading.Title).FontSize(PdfTheme.TitleFontSize).Bold();
                if (Heading.Number is { Length: > 0 } number)
                {
                    t.Span("  " + number).FontSize(PdfTheme.TitleFontSize).SemiBold();
                }
            });
            if (Heading.Date is { } date)
            {
                row.ConstantItem(120).AlignRight().AlignBottom().Text("Data: " + PdfTheme.Date(date));
            }
        });
        if (PdfMarks.BandText(Heading.Mark, Heading.Notice) is not null)
        {
            col.Item().PaddingTop(4).Element(e => PdfMarks.Band(e, Heading.Mark, Heading.Notice));
        }
    });

    private void ComposeFooter(IContainer c) => c.BorderTop(1).BorderColor(PdfTheme.Line).PaddingTop(4).AlignCenter().Text(t =>
    {
        t.DefaultTextStyle(s => s.FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted));
        t.Span(Issued(Frame));
        t.Span(" · Página ");
        t.CurrentPageNumber();
        t.Span(" de ");
        t.TotalPages();
    });

    /// <summary>"Emitido por {usuário} em {dd/MM/aaaa HH:mm}" (horário de São Paulo).</summary>
    public static string Issued(PdfFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return $"Emitido por {frame.IssuedBy} em {PdfTheme.DateTime(frame.IssuedAt)}";
    }

    /// <summary>Bloco de dados "rótulo: valor" em colunas, no fundo <see cref="PdfTheme.Surface"/>; valor vazio vira "—".</summary>
    protected static void Fields(IContainer c, int columns, params (string Label, string? Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        c.Background(PdfTheme.Surface).Padding(8).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                for (var i = 0; i < columns; i++)
                {
                    cols.RelativeColumn();
                }
            });
            foreach (var (label, value) in fields)
            {
                table.Cell().PaddingBottom(4).Column(cell =>
                {
                    cell.Item().Text(label).FontSize(PdfTheme.SmallFontSize).FontColor(PdfTheme.Muted);
                    cell.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value);
                });
            }
        });
    }

    /// <summary>Título de seção.</summary>
    protected static void Section(IContainer c, string title) => c.PaddingTop(6).Text(title).FontSize(11).SemiBold();

    /// <summary>Célula de cabeçalho de tabela (repetido na quebra de página pelo <c>table.Header</c>).</summary>
    protected static IContainer HeaderCell(IContainer c) => c.Background(PdfTheme.Surface).BorderBottom(1).BorderColor(PdfTheme.Line).PaddingVertical(4).PaddingHorizontal(3);

    protected static IContainer BodyCell(IContainer c) => c.BorderBottom(0.5f).BorderColor(PdfTheme.Surface).PaddingVertical(3).PaddingHorizontal(3);

    /// <summary>Linha de assinatura com o rótulo embaixo.</summary>
    protected static void Signature(IContainer c, string label) => c.Column(col =>
    {
        col.Item().Height(28);
        col.Item().BorderTop(1).BorderColor(PdfTheme.Line).PaddingTop(4).Text(label).FontSize(PdfTheme.SmallFontSize);
    });
}
