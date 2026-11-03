using System.Net;
using System.Text.RegularExpressions;
using BemAqui.Inventory.Application.Documents;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Inventory;

public partial class InventoryCountTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex PdfSpaces();

    private static async Task<string> PdfTextAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return PdfSpaces().Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), string.Empty);
    }

    /// <summary>
    /// EXP-04 CA 12 e 13: a folha de contagem sai em contagem <b>sem o saldo da foto</b> (contagem cega) e o resultado é recusado com 409 até
    /// fechar; fechado, o resultado traz a foto, o contado e a diferença, a folha passa a 409 e o ajuste gerado (pendente) sai como Prévia.
    /// </summary>
    [Fact]
    public async Task Folha_de_contagem_sem_saldo_e_resultado_so_depois_de_fechar()
    {
        var (_, owner) = await ActorAsync("owner");
        var location = await LocationAsync();
        var item = await ItemAsync();
        await MoveAsync(item, location, 4817.5m);
        var code = await WithDbAsync(db => db.Items.Where(i => i.Id == item).Select(i => i.Code).SingleAsync());
        var count = await OkAsync(await owner.SendAsync(Command(Counts, Open(location))), HttpStatusCode.Created);
        var number = count.GetProperty("number").GetString()!;

        var sheet = await PdfTextAsync(owner, $"{Counts}/{Id(count)}/count-sheet/pdf");
        sheet.Should().Contain("Folhadecontagem").And.Contain(number).And.Contain(code).And.Contain("Quantidadecontada").And.Contain("Contadopor");
        sheet.Should().NotContainAny(["4817", "4.817"], "CA 12: contagem cega, sem o saldo do sistema");

        var early = await owner.GetAsync($"{Counts}/{Id(count)}/pdf");
        early.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Detail(await JsonAsync(early)).Should().Be(InventoryPdf.CloseFirst);

        var counting = await OkAsync(await owner.GetAsync($"{Counts}/{Id(count)}/counting"));
        await OkAsync(await owner.SendAsync(CountRequest(Id(count), LineOf(counting, item), 4810m)));
        var closed = await CloseAsync(owner, await OkAsync(await owner.GetAsync($"{Counts}/{Id(count)}")));

        var result = await PdfTextAsync(owner, $"{Counts}/{Id(count)}/pdf");
        result.Should().Contain("Resultadodoinventário").And.Contain("4.817,5").And.Contain("4.810").And.Contain("-7,5").And.NotContain("CANCELADO");
        (await owner.GetAsync($"{Counts}/{Id(count)}/count-sheet/pdf")).StatusCode.Should().Be(HttpStatusCode.Conflict, "a folha só sai em contagem");

        var adjustment = closed.GetProperty("adjustmentId").GetGuid();
        (await PdfTextAsync(owner, $"{Adjustments}/{adjustment}/pdf")).Should().Contain("Ajustedeestoque").And.Contain("PRÉVIA").And.Contain("Aguardandoaprovação");
        (await owner.GetAsync($"{Counts}/{Guid.CreateVersion7()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>CA 3 e 12: a folha de contagem não tem saldo, diferença nem custo; a folha de separação não tem custo nem valor (não há de onde tirar).</summary>
    [Fact]
    public void Modelos_da_folha_de_contagem_e_da_separacao_nao_tem_saldo_nem_custo()
    {
        string[] forbidden = ["System", "Qty", "Balance", "Cost", "Value", "Difference", "Amount", "OnHand"];
        new[] { typeof(InventoryCountSheetModel), typeof(InventoryCountSheetLine), typeof(RequisitionPdfModel), typeof(RequisitionPdfLine) }
            .SelectMany(t => t.GetProperties()).Select(p => p.Name)
            .Should().NotContain(n => forbidden.Any(f => n.Contains(f, StringComparison.Ordinal)));
        InventoryPdf.SheetOrder([new("B", "b", "UN", null, false), new("C", "c", "UN", null, true), new("A", "a", "UN", null, false)]).Select(l => l.ItemCode)
            .Should().Equal("C", "A", "B");
    }
}
