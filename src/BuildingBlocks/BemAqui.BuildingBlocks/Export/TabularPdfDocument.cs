using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.BuildingBlocks.Export;

/// <summary>
/// PDF de lista (ADR 0045 §2) no <see cref="PdfDocumentFrame"/>: título da tela com a data, a linha "Filtros: …" e a contagem, as notas da
/// tela (EXP-02: destaques do fluxo de caixa) e a tabela com o cabeçalho repetido em cada página e os números à direita. Mais de
/// <see cref="LandscapeAbove"/> colunas vai em paisagem.
/// </summary>
public sealed class TabularPdfDocument : PdfDocumentFrame
{
    public const int LandscapeAbove = 6;

    /// <summary>Recuo por nível da coluna marcada (hierarquia do DRE), em pontos.</summary>
    public const float IndentPerLevel = 8;

    private readonly ExportTable _table;

    public TabularPdfDocument(PdfFrame frame, ExportTable table)
        : base(frame, Header(table)) => _table = table;

    private static PdfDocumentHeader Header(ExportTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return new PdfDocumentHeader(table.Title, null, DateOnly.FromDateTime(BusinessTime.ToBusiness(table.IssuedAt).DateTime), PdfMark.None,
            table.Landscape ?? table.Columns.Count > LandscapeAbove);
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
        foreach (var note in _table.Notes)
        {
            col.Item().Text(note);
        }

        if (_table.Rows.Count == 0)
        {
            col.Item().PaddingTop(12).Text("Nenhuma linha para os filtros.");
            return;
        }

        col.Item().Element(c => Draw(c, _table.Columns, _table.Rows, _table.RowStyles));
    });

    /// <summary>
    /// A tabela tipada (cabeçalho repetido na quebra, números à direita, negrito e recuo da linha com destaque): também a dos documentos
    /// próprios que mostram uma lista (extrato, conciliação, DRE).
    /// </summary>
    public static void Draw(IContainer container, IReadOnlyList<ExportColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<ExportRowStyle?>? styles = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        container.Table(t =>
        {
            t.ColumnsDefinition(cols =>
            {
                foreach (var column in columns)
                {
                    cols.RelativeColumn(Weight(column.Type));
                }
            });
            t.Header(h =>
            {
                foreach (var column in columns)
                {
                    var cell = HeaderCell(h.Cell());
                    (XlsxTabularExporter.IsNumeric(column.Type) ? cell.AlignRight() : cell).Text(column.Header).SemiBold();
                }
            });
            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                var style = styles is not null && r < styles.Count ? styles[r] : null;
                for (var c = 0; c < columns.Count; c++)
                {
                    var type = columns[c].Type;
                    var cell = BodyCell(t.Cell());
                    if (style is { Indent: > 0 } && columns[c].Indented)
                    {
                        cell = cell.PaddingLeft(style.Indent * IndentPerLevel);
                    }

                    var value = c < row.Count ? row[c] : null;
                    var text = (XlsxTabularExporter.IsNumeric(type) ? cell.AlignRight() : cell).Text(Format(type, value));
                    if (style is { Bold: true })
                    {
                        text.SemiBold();
                    }

                    if (style is { CriticalWhenNegative: true } && value is decimal and < 0m)
                    {
                        text.FontColor(PdfTheme.CritInk);
                    }
                }
            }
        });
    }

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
