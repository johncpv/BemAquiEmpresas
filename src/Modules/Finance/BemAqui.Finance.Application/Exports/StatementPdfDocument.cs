using BemAqui.BuildingBlocks.Export;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Time;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace BemAqui.Finance.Application.Exports;

/// <summary>O extrato da tela, a conta como a tela mostra (agência e número mascarados sem <c>finance.edit</c>) e até quando está conciliado.</summary>
public sealed record StatementPdfModel(StatementResult Statement, BankAccountDetail? Account, DateOnly? ReconciledUntil);

/// <summary>
/// "Extrato da conta" do período (EXP-02 CA 8) no modelo único: a conta, o período, o saldo de partida, os movimentos (data, descrição,
/// origem, entrada, saída, saldo e situação), os totais de entradas e saídas, o saldo final e "Movimentos conciliados até". Sem saldo
/// inicial informado, a faixa de alerta no topo.
/// </summary>
public sealed class StatementPdfDocument(PdfFrame frame, StatementPdfModel model, ExportTable table)
    : PdfDocumentFrame(frame, new PdfDocumentHeader("Extrato da conta", null, DateOnly.FromDateTime(BusinessTime.ToBusiness(frame.IssuedAt).DateTime)))
{
    public const string OpeningMissingText = "Saldo inicial da conta não informado: os saldos podem estar errados.";

    protected override void ComposeContent(IContainer container) => container.Column(col =>
    {
        var s = model.Statement;
        var a = model.Account;
        col.Spacing(8);
        if (s.OpeningMissing)
        {
            col.Item().Background(PdfTheme.WarnSurface).PaddingVertical(3).PaddingHorizontal(6).Text(OpeningMissingText).SemiBold().FontColor(PdfTheme.WarnInk);
        }

        col.Item().Element(c => Fields(c, 3,
            ("Conta", s.AccountName),
            ("Banco", a is null ? null : a.BankName ?? a.BankCode),
            ("Agência e conta", a is null ? null : string.Join(" · ", new[] { a.Agency, a.Account }.Where(x => !string.IsNullOrWhiteSpace(x)))),
            ("Período", $"{PdfTheme.Date(s.From)} a {PdfTheme.Date(s.To)}"),
            (s.StartLabel, PdfTheme.Money(s.StartBalance)),
            ("Saldo final", PdfTheme.Money(s.EndBalance))));
        col.Item().Element(c => Section(c, "Movimentos"));
        if (s.Lines.Items.Count == 0)
        {
            col.Item().Text("Nenhuma movimentação no período.");
        }
        else
        {
            col.Item().Element(c => TabularPdfDocument.Draw(c, table.Columns, table.Rows, table.RowStyles));
        }

        var inflow = s.Lines.Items.Where(l => l.Amount > 0).Sum(l => l.Amount);
        var outflow = -s.Lines.Items.Where(l => l.Amount < 0).Sum(l => l.Amount);
        col.Item().Element(c => Fields(c, 3, ("Total de entradas", PdfTheme.Money(inflow)), ("Total de saídas", PdfTheme.Money(outflow)), ("Saldo final", PdfTheme.Money(s.EndBalance))));
        if (model.ReconciledUntil is { } until)
        {
            col.Item().Text($"Movimentos conciliados até {PdfTheme.Date(until)}.").FontColor(PdfTheme.Muted);
        }
    });
}
