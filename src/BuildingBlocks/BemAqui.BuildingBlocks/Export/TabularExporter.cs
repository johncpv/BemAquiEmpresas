using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using ClosedXML.Excel;
using ClosedXML.Graphics;

namespace BemAqui.BuildingBlocks.Export;

/// <summary>Gera o arquivo de uma <see cref="ExportTable"/> num formato (ADR 0045 §2). Um por formato, escolhido pelo <see cref="Format"/>.</summary>
public interface ITabularExporter
{
    ExportFormat Format { get; }

    string ContentType { get; }

    string Extension { get; }

    Task<byte[]> ExportAsync(ExportTable table, CancellationToken cancellationToken = default);
}

/// <summary>
/// Planilha xlsx com o ClosedXML (MIT; ADR 0045 §2): aba com o título da tela, cabeçalho congelado e com autofiltro, células tipadas (dinheiro
/// em reais, quantidade com até 4 casas, data, data e hora de São Paulo, percentual), largura por tipo e aba "Filtros" com os filtros, quem
/// emitiu e quando. Texto é sempre texto: nunca vira fórmula, e o que começa com <c>= + - @</c>, tabulação ou retorno ganha o prefixo de
/// texto da própria planilha (a mesma regra do <see cref="SafeCsv"/>, sem mudar o valor).
/// </summary>
public sealed class XlsxTabularExporter : ITabularExporter
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    internal const string MoneyFormat = "[$R$-416] #,##0.00";
    internal const string QuantityFormat = "#,##0.00##";
    internal const string DateFormat = "dd/mm/yyyy";
    internal const string DateTimeFormat = "dd/mm/yyyy hh:mm";
    internal const string PercentFormat = "0.00%";

    // Sem fonte do sistema (a imagem Alpine não tem nenhuma, ADR 0022): o ClosedXML mede texto com a Archivo embutida.
    private static readonly Lazy<bool> Configured = new(() =>
    {
        var assembly = typeof(XlsxTabularExporter).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Archivo-Regular.ttf", StringComparison.OrdinalIgnoreCase));
        LoadOptions.DefaultGraphicEngine = DefaultGraphicEngine.CreateOnlyWithFonts(assembly.GetManifestResourceStream(name)!);
        return true;
    });

    public ExportFormat Format => ExportFormat.Xlsx;

    public string ContentType => XlsxContentType;

    public string Extension => "xlsx";

    public Task<byte[]> ExportAsync(ExportTable table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Build(table), cancellationToken);
    }

    internal static byte[] Build(ExportTable table)
    {
        _ = Configured.Value;
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName(table.Title));
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var header = sheet.Cell(1, c + 1);
            header.Value = table.Columns[c].Header;
            header.Style.Font.Bold = true;
            header.Style.Fill.BackgroundColor = XLColor.FromHtml(PdfTheme.Surface);
            if (IsNumeric(table.Columns[c].Type))
            {
                header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            for (var c = 0; c < table.Columns.Count; c++)
            {
                Write(sheet.Cell(r + 2, c + 1), table.Columns[c].Type, c < row.Count ? row[c] : null);
            }
        }

        for (var c = 0; c < table.Columns.Count; c++)
        {
            sheet.Column(c + 1).Width = Width(table.Columns[c], table.Rows.Select(r => c < r.Count ? r[c] : null));
        }

        if (table.Columns.Count > 0)
        {
            sheet.SheetView.FreezeRows(1);
            sheet.Range(1, 1, Math.Max(1, table.Rows.Count + 1), table.Columns.Count).SetAutoFilter();
        }

        Filters(workbook.Worksheets.Add("Filtros"), table);
        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    /// <summary>Célula tipada; texto perigoso (fórmula, DDE) fica como texto com o prefixo de texto.</summary>
    internal static void Write(IXLCell cell, ExportColumnType type, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                cell.Value = text;
                if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
                {
                    cell.Style.IncludeQuotePrefix = true;
                }

                return;
            case DateOnly date:
                cell.Value = date.ToDateTime(TimeOnly.MinValue);
                cell.Style.NumberFormat.Format = DateFormat;
                return;
            case DateTimeOffset moment:
                cell.Value = BusinessTime.ToBusiness(moment).DateTime;
                cell.Style.NumberFormat.Format = DateTimeFormat;
                return;
            case decimal number:
                Number(cell, type, number);
                return;
            case int number:
                Number(cell, type, number);
                return;
            default:
                cell.Value = Convert.ToString(value, PdfTheme.PtBr);
                return;
        }
    }

    private static void Number(IXLCell cell, ExportColumnType type, decimal number)
    {
        cell.Value = type == ExportColumnType.Percent ? number / 100m : number;
        var format = type switch
        {
            ExportColumnType.Money => MoneyFormat,
            ExportColumnType.Quantity => QuantityFormat,
            ExportColumnType.Percent => PercentFormat,
            _ => null,
        };
        if (format is not null)
        {
            cell.Style.NumberFormat.Format = format;
        }
    }

    internal static bool IsNumeric(ExportColumnType type) => type is ExportColumnType.Money or ExportColumnType.Quantity or ExportColumnType.Percent;

    /// <summary>Largura fixa por tipo; texto pelo maior conteúdo, de 12 a 60 caracteres (sem medir fonte).</summary>
    internal static double Width(ExportColumn column, IEnumerable<object?> values)
    {
        var header = column.Header.Length + 3;
        return column.Type switch
        {
            ExportColumnType.Money => Math.Max(16, header),
            ExportColumnType.Quantity => Math.Max(12, header),
            ExportColumnType.Date => Math.Max(12, header),
            ExportColumnType.DateTime => Math.Max(17, header),
            ExportColumnType.Percent => Math.Max(10, header),
            _ => Math.Clamp(Math.Max(header, values.Select(v => v?.ToString()?.Length ?? 0).DefaultIfEmpty(0).Max() + 2), 12, 60),
        };
    }

    private static void Filters(IXLWorksheet sheet, ExportTable table)
    {
        var lines = new List<(string Label, object? Value)>
        {
            ("Lista", table.Title),
            ("Emitido por", table.IssuedBy),
            ("Emitido em", table.IssuedAt),
            ("Linhas", table.Rows.Count),
        };
        lines.AddRange(table.Filters.Select(f => (f.Label, (object?)(string.IsNullOrWhiteSpace(f.Value) ? "Todos" : f.Value))));
        for (var i = 0; i < lines.Count; i++)
        {
            var label = sheet.Cell(i + 1, 1);
            label.Value = lines[i].Label;
            label.Style.Font.Bold = true;
            Write(sheet.Cell(i + 1, 2), ExportColumnType.Text, lines[i].Value);
        }

        sheet.Column(1).Width = 16;
        sheet.Column(2).Width = 48;
    }

    /// <summary>Nome de aba válido: até 31 caracteres, sem <c>: \ / ? * [ ]</c>.</summary>
    internal static string SheetName(string title)
    {
        var clean = new string(title.Where(ch => ch is not (':' or '\\' or '/' or '?' or '*' or '[' or ']')).ToArray()).Trim();
        clean = clean.Length == 0 ? "Lista" : clean;
        return clean.Length > 31 ? clean[..31] : clean;
    }
}

/// <summary>A mesma tabela como PDF de lista no modelo único (<see cref="TabularPdfDocument"/>): paisagem com mais de 6 colunas.</summary>
public sealed class PdfTabularExporter(IPdfRenderer renderer, PdfFrameFactory frames) : ITabularExporter
{
    public ExportFormat Format => ExportFormat.Pdf;

    public string ContentType => "application/pdf";

    public string Extension => "pdf";

    public async Task<byte[]> ExportAsync(ExportTable table, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(table);
        var frame = await frames.CreateAsync(cancellationToken) with { IssuedBy = table.IssuedBy, IssuedAt = table.IssuedAt };
        return await renderer.RenderAsync(new TabularPdfDocument(frame, table), cancellationToken);
    }
}
