using System.Net;
using BemAqui.Finance.Domain;
using ClosedXML.Excel;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>EXP-02: conciliação em abas e relatório com "Prévia" (CA 10 e 11); extrato do banco com a contraparte mascarada como a tela (CA 9).</summary>
public partial class ReconciliationTests
{
    [Fact]
    public async Task Conciliacao_exporta_os_blocos_em_abas_o_relatorio_em_previa_e_o_extrato_do_banco_mascarado()
    {
        var (_, _, account) = await AccountAsync();
        var (_, viewer, _) = await UserAsync(false, "finance.view");
        await LinesAsync(account, StatementSource.Api, null, new L(1000m, -5), new L(-45.90m, -4, "TARIFA", StatementLineKind.Fee, "12345678000190"));
        await TxAsync(account, 1000m, -5, "Recebimento CR-1");
        await TxAsync(account, -10m, -3, "Sem linha no banco");

        var xlsx = await viewer.GetAsync(Account(account) + "/reconciliation/export" + Range + "&format=xlsx");
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK, await xlsx.Content.ReadAsStringAsync());
        using (var workbook = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync())))
        {
            workbook.Worksheets.Select(w => w.Name).Should().Equal("Conciliados", "Sugestões", "Sem par no banco", "No sistema, não no banco", "Resumo", "Filtros");
            workbook.Worksheet("Sugestões").RowsUsed().Skip(1).Select(r => r.Cell(2).GetString()).Should().Contain("PIX RECEBIDO");
            workbook.Worksheet("Sem par no banco").RowsUsed().Skip(1).Select(r => r.Cell(2).GetString()).Should().Contain("TARIFA");
            var stale = workbook.Worksheet("No sistema, não no banco").RowsUsed().Skip(1).Single(r => r.Cell(2).GetString() == "Sem linha no banco");
            stale.Cell(5).GetString().Should().Be("Não");
            workbook.Worksheet("Resumo").Column(1).CellsUsed().Select(c => c.GetString()).Should().Contain(["Saldo do banco", "Diferença", "Conciliada até", "Fechada até"]);
        }

        var pdf = await viewer.GetAsync(Account(account) + "/reconciliation/pdf" + Range);
        pdf.StatusCode.Should().Be(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync());
        pdf.Content.Headers.ContentDisposition!.FileName.Should().Be($"conciliacao-{Iso(Today)}.pdf");
        using (var document = PdfDocument.Open(await pdf.Content.ReadAsByteArrayAsync()))
        {
            var text = string.Concat(string.Join(" ", document.GetPages().Select(p => p.Text)).Where(c => !char.IsWhiteSpace(c)));
            text.Should().Contain("Relatóriodeconciliação").And.Contain("PRÉVIA", "período não fechado").And.Contain("Períodoaindanãofechado").And.Contain("Sugestões");
        }

        var lines = await viewer.GetAsync(Account(account) + "/statement-lines/export" + Range + "&format=xlsx");
        lines.StatusCode.Should().Be(HttpStatusCode.OK, await lines.Content.ReadAsStringAsync());
        using var bank = new XLWorkbook(new MemoryStream(await lines.Content.ReadAsByteArrayAsync()));
        var fee = bank.Worksheet("Extrato do banco").RowsUsed().Skip(1).Single(r => r.Cell(2).GetString() == "TARIFA");
        fee.Cell(3).GetString().Should().Be("Tarifa");
        fee.Cell(5).GetString().Should().NotBeEmpty().And.NotContain("5678", "sem finance.edit a contraparte sai mascarada, como na tela");
        fee.Cell(7).GetString().Should().Be("API");
        fee.Cell(9).GetString().Should().Be("Pendente");
    }
}
