using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Domain;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>EXP-02: exportação do a receber (filtro inteiro e totais do rodapé, teto) e do extrato da conta (planilha e PDF do período).</summary>
public partial class ReceivablesTests
{
    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", string.Empty);

    [Fact]
    public async Task A_receber_exporta_as_linhas_do_filtro_com_os_totais_do_rodape_e_recusa_acima_do_teto()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        await ReceivableAsync(owner, party, 1_000m, Today.AddDays(10));
        await ReceivableAsync(owner, party, 250.5m, Today.AddDays(-5));
        await ReceivableAsync(owner, party, 300m, Today.AddDays(20));
        var (viewerId, viewer) = await ActorAsync("view");

        var list = await JsonAsync(await viewer.GetAsync($"/api/v1/finance/receivables?partyId={party}&pageSize=1"));
        var total = list.GetProperty("page").GetProperty("total").GetInt32();
        total.Should().Be(3);

        var response = await viewer.GetAsync($"/api/v1/finance/receivables/export?format=xlsx&partyId={party}&sort=-amount");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentDisposition!.FileName.Should().Be($"contas-a-receber-{Iso(Today)}.xlsx");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet("Contas a receber");
        sheet.Cell(1, 6).GetString().Should().Be("Vencimento");
        var rows = sheet.RowsUsed().Skip(1).ToList();
        rows.Should().HaveCount(total + 2, "todas as linhas do filtro (não só a página) mais Em aberto e Vencido");
        rows.Take(total).Select(r => r.Cell(7).GetValue<decimal>()).Should().Equal([1_000m, 300m, 250.5m], "a ordem da tela");
        rows[1].Cell(6).DataType.Should().Be(XLDataType.DateTime);
        rows.Single(r => r.Cell(7).GetValue<decimal>() == 250.5m).Cell(10).GetString().Should().Be("Vencido");
        rows[^2].Cell(1).GetString().Should().Be("Em aberto");
        rows[^2].Cell(9).GetValue<decimal>().Should().Be(list.GetProperty("totals").GetProperty("open").GetDecimal());
        rows[^1].Cell(1).GetString().Should().Be("Vencido");
        rows[^1].Cell(9).GetValue<decimal>().Should().Be(list.GetProperty("totals").GetProperty("overdue").GetDecimal()).And.Be(250.5m);
        rows[^1].Cell(1).Style.Font.Bold.Should().BeTrue();
        workbook.Worksheet("Filtros").Column(1).CellsUsed().Select(c => c.GetString()).Should().Contain("Cliente");
        (await QueryAsync(db => db.AuditEntries.CountAsync(a => a.Entity == "Receivable" && a.EntityId == "list" && a.Action == AuditActions.Exported && a.UserId == viewerId))).Should().Be(1);

        await using var limited = Factory.WithWebHostBuilder(b => b.UseSetting("Export:MaxPdfRows", "4"));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = viewer.DefaultRequestHeaders.Authorization;
        var tooLarge = await client.GetAsync($"/api/v1/finance/receivables/export?format=pdf&partyId={party}");
        tooLarge.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(tooLarge)).GetProperty("code").GetString().Should().Be("export_too_large");
    }

    [Fact]
    public async Task Extrato_exporta_saldo_de_partida_movimentos_e_saldo_final_e_o_PDF_do_periodo()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var bank = await BankAsync(owner);
        var receivable = await ReceivableAsync(owner, party, 500m);
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{receivable.GetProperty("id").GetGuid()}/settlements", Settle(bank, 500m, Version(receivable))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var (_, viewer) = await ActorAsync("view");
        var period = $"from={Iso(Today.AddDays(-1))}&to={Iso(Today)}";

        var xlsx = await viewer.GetAsync($"/api/v1/finance/bank-accounts/{bank}/statement/export?format=xlsx&{period}");
        xlsx.StatusCode.Should().Be(HttpStatusCode.OK, await xlsx.Content.ReadAsStringAsync());
        using var workbook = new XLWorkbook(new MemoryStream(await xlsx.Content.ReadAsByteArrayAsync()));
        var rows = workbook.Worksheet("Extrato da conta").RowsUsed().Skip(1).ToList();
        rows.Should().HaveCount(3);
        rows[0].Cell(2).GetString().Should().BeOneOf("Saldo inicial", "Saldo anterior");
        rows[0].Cell(7).GetValue<decimal>().Should().Be(0m);
        rows[1].Cell(3).GetString().Should().Be("Recebimento");
        rows[1].Cell(5).GetValue<decimal>().Should().Be(500m);
        rows[1].Cell(9).GetString().Should().Be("—");
        rows[2].Cell(2).GetString().Should().Be("Saldo final");
        rows[2].Cell(7).GetValue<decimal>().Should().Be(500m);

        var pdf = await viewer.GetAsync($"/api/v1/finance/bank-accounts/{bank}/statement/pdf?{period}");
        pdf.StatusCode.Should().Be(HttpStatusCode.OK, await pdf.Content.ReadAsStringAsync());
        pdf.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        pdf.Content.Headers.ContentDisposition!.FileName.Should().Be($"extrato-{Iso(Today)}.pdf");
        using var document = PdfDocument.Open(await pdf.Content.ReadAsByteArrayAsync());
        var text = Flat(string.Join(" ", document.GetPages().Select(p => p.Text)));
        text.Should().Contain(Flat("Extrato da conta")).And.Contain(Flat("Saldo inicial da conta não informado")).And.Contain(Flat("Total de entradas"))
            .And.Contain("500,00").And.Contain(Flat("Saldo final"));
    }
}
