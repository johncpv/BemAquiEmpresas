using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Application.Reconciliation;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Finance.Application.Exports;

/// <summary>
/// "Relatório de conciliação" da conta e do período (EXP-02 CA 11), em paisagem: o resumo no topo (saldos, diferença, conciliada e fechada
/// até) e os blocos da tela em seções (conciliados, sugestões, sem par no banco, no sistema e não no banco). Com o período ainda não
/// fechado, a marca "Prévia" em todas as páginas.
/// </summary>
public sealed class ReconciliationPdfDocument(PdfFrame frame, ReconciliationView view, ExportTable table)
    : PdfDocumentFrame(frame, new PdfDocumentHeader("Relatório de conciliação", null, DateOnly.FromDateTime(BusinessTime.ToBusiness(frame.IssuedAt).DateTime),
        FinanceExports.IsPreview(view) ? PdfMark.Preview : PdfMark.None, Landscape: true))
{
    public const string PreviewText = "Período ainda não fechado: os números podem mudar.";

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var t = view.Totals;
        col.Spacing(8);
        if (FinanceExports.IsPreview(view))
        {
            col.Item().Text(PreviewText).FontColor(PdfTheme.Muted);
        }

        col.Item().Element(c => Fields(c, 4,
            ("Conta", view.AccountName),
            ("Período", $"{PdfTheme.Date(view.From)} a {PdfTheme.Date(view.To)}"),
            ("Conciliada até", view.ReconciledUntil is { } r ? PdfTheme.Date(r) : null),
            ("Fechada até", view.ClosedUntil is { } f ? PdfTheme.Date(f) : null),
            ("Saldo do banco", t.BankBalance is { } b ? PdfTheme.Money(b) : "Sem saldo do banco no período"),
            ("Saldo do sistema", t.SystemBalance is { } s ? PdfTheme.Money(s) : null),
            ("Diferença", t.Difference is { } d ? PdfTheme.Money(d) : null),
            ("Linhas", $"{t.Pending} pendentes · {t.Reconciled} conciliadas · {t.Ignored} ignoradas")));
        Block(col, "Conciliados", table.Columns, table.Rows);
        foreach (var sheet in table.Sheets.Where(s => s.Name != "Resumo"))
        {
            Block(col, sheet.Name, sheet.Columns, sheet.Rows);
        }
    });

    private static void Block(ColumnDescriptor col, string title, IReadOnlyList<ExportColumn> columns, IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        col.Item().Element(c => Section(c, $"{title} ({rows.Count})"));
        if (rows.Count == 0)
        {
            col.Item().Text("Nada neste bloco.").FontColor(PdfTheme.Muted);
            return;
        }

        col.Item().Element(c => TabularPdfDocument.Draw(c, columns, rows));
    }
}
