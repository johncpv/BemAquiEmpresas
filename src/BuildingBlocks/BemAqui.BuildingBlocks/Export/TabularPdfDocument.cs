using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.BuildingBlocks.Export;

/// <summary>
/// PDF de lista (ADR 0045 §2) no <see cref="PdfDocumentFrame"/>: título da tela com a data, a linha "Filtros: …" e a contagem, e a tabela com
/// o cabeçalho repetido em cada página e os números à direita. Mais de <see cref="LandscapeAbove"/> colunas vai em paisagem.
/// </summary>
public sealed class TabularPdfDocument : PdfDocumentFrame
{
    public const int LandscapeAbove = 6;

    private readonly ExportTable _table;

    public TabularPdfDocument(PdfFrame frame, ExportTable table)
        : base(frame, Header(table)) => _table = table;

    private static PdfDocumentHeader Header(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new PdfDocumentHeader(table.Title, null, DateOnly.FromDateTime(BusinessTime.ToBusiness(table.IssuedAt).DateTime), PdfMark.None, table.Columns.Count > LandscapeAbove);
    }

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        col.Spacing(8);
        col.Item().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontColor(PdfTheme.Muted));
            t.Span("Filtros: ").SemiBold();
            t.Span(FiltersText(_table.Filters));
            t.Span(_table.Rows.Count == 1 ? " · 1 linha" : $" · {_table.Rows.Count.ToString("N0", PdfTheme.PtBr)} linhas");
        });
        if (_table.Rows.Count == 0)
        {
            col.Item().PaddingTop(12).Text("Nenhuma linha para os filtros.");
            return;
        }

        col.Item().Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                foreach (var column in _table.Columns)
                {
                    cols.RelativeColumn(Weight(column.Type));
                }
            });
            t.Header(h =>
            {
                foreach (var column in _table.Columns)
                {
                    var cell = HeaderCell(h.Cell());
                    (XlsxTabularExporter.IsNumeric(column.Type) ? cell.AlignRight() : cell).Text(column.Header).SemiBold();
                }
            });
            foreach (var row in _table.Rows)
            {
                for (var c = 0; c < _table.Columns.Count; c++)
                {
                    var type = _table.Columns[c].Type;
                    var cell = BodyCell(t.Cell());
                    (XlsxTabularExporter.IsNumeric(type) ? cell.AlignRight() : cell).Text(Format(type, c < row.Count ? row[c] : null));
                }
            }
        });
    });

    /// <summary>"Situação: Em aberto; Busca: aluguel" (vazio é "Todos"); sem filtro, "nenhum".</summary>
    public static string FiltersText(IReadOnlyList<ExportFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);
        var set = filters.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => $"{f.Label}: {f.Value}").ToList();
        return set.Count == 0 ? "nenhum" : string.Join("; ", set);
    }

    /// <summary>Texto da célula no padrão pt-BR.</summary>
    public static string Format(ExportColumnType type, object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        DateOnly date => PdfTheme.Date(date),
        DateTimeOffset moment => PdfTheme.DateTime(moment),
        decimal n => type switch
        {
            ExportColumnType.Money => PdfTheme.Money(n),
            ExportColumnType.Quantity => PdfTheme.Quantity(n),
            ExportColumnType.Percent => PdfTheme.Percent(n),
            _ => n.ToString("N2", PdfTheme.PtBr),
        },
        int n => n.ToString("N0", PdfTheme.PtBr),
        _ => Convert.ToString(value, PdfTheme.PtBr) ?? string.Empty,
    };

    private static float Weight(ExportColumnType type) => type switch
    {
        ExportColumnType.Text => 3f,
        ExportColumnType.Money => 1.6f,
        ExportColumnType.DateTime => 1.6f,
        ExportColumnType.Date => 1.2f,
        _ => 1f,
    };
}
