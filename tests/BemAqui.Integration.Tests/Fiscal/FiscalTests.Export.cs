using System.Net;
using ClosedXML.Excel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Fiscal;

/// <summary>EXP-05 CA 16: o livro simples das Emitidas sai com chave, CFOP e documento, e a última linha soma só as autorizadas.</summary>
public partial class FiscalTests
{
    [Fact]
    public async Task Livro_simples_das_emitidas_traz_a_nota_autorizada_e_soma_so_as_autorizadas()
    {
        await ReadyToIssueAsync();
        var (owner, sale, draft) = await DraftAsync();
        var issued = await Ok(await IssueInvoiceAsync(owner, draft));
        issued.GetProperty("status").GetString().Should().Be("Authorized");
        var invoice = await fixture.InvoiceOfAsync(fixture.Tenancy.TenantA, draft.GetProperty("id").GetGuid());

        var response = await owner.GetAsync($"{Invoices}/export?format=xlsx&projectId={sale.Project}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet("Notas de saída emitidas");
        var headers = sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        headers.Should().ContainInOrder("Número", "Série", "Chave", "Emissão", "Destinatário", "CNPJ/CPF", "CFOP", "Produtos", "ICMS", "IPI", "Total", "Situação");
        int Col(string name) => headers.IndexOf(name) + 1;
        var rows = sheet.RowsUsed().Skip(1).ToList();
        rows.Should().HaveCount(2, "a nota do projeto e a linha do total");
        var row = rows[0];
        row.Cell(Col("Número")).GetString().Should().Be(invoice.Number!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        row.Cell(Col("Chave")).GetString().Should().Be(invoice.AccessKey);
        row.Cell(Col("CFOP")).GetString().Should().Be("5101");
        row.Cell(Col("CNPJ/CPF")).GetString().Should().NotBeNullOrEmpty();
        row.Cell(Col("Situação")).GetString().Should().Be("Autorizada");
        row.Cell(Col("Total")).GetValue<decimal>().Should().Be(invoice.TotalAmount);
        rows[^1].Cell(Col("Número")).GetString().Should().Be("Total das autorizadas");
        rows[^1].Cell(Col("Total")).GetValue<decimal>().Should().Be(invoice.TotalAmount);
        workbook.Worksheet("Filtros").Column(1).CellsUsed().Select(c => c.GetString()).Should().Contain("Projeto");
    }
}
