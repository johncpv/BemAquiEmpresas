using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Purchasing.Application.Documents;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Purchasing;

public partial class GoodsReceiptTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex PdfSpaces();

    private static async Task<string> ReceiptPdfAsync(HttpClient client, Guid receiptId)
    {
        var response = await client.GetAsync($"{Receipts}/{receiptId}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return PdfSpaces().Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), string.Empty);
    }

    /// <summary>
    /// EXP-04 CA 9: o comprovante traz o pedido, o item com lote e as assinaturas, <b>sem</b> o custo de entrada nem o preço (que a tela mostra),
    /// e cada download entra na auditoria; estornado, sai com "Cancelado" e o motivo.
    /// </summary>
    [Fact]
    public async Task Comprovante_de_recebimento_sem_custo_e_estornado_com_Cancelado()
    {
        var (ownerId, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await LotItemAsync(tracksLot: true);
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 3m, 1234.56m, null));
        var receipt = await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, item), 3m, "L-PDF-7")));
        var receiptId = receipt.GetProperty("id").GetGuid();
        var number = receipt.GetProperty("number").GetString()!;

        var text = await ReceiptPdfAsync(owner, receiptId);
        text.Should().Contain("Comprovantederecebimento").And.Contain(number).And.Contain(order.GetProperty("number").GetString())
            .And.Contain("L-PDF-7").And.Contain("Recebidopor").And.Contain("Conferidopor");
        text.Should().NotContainAny("1.234,56", "3.703,68", "R$", "Custo", "custo", "CANCELADO");
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == GoodsReceiptDocumentService.AuditEntity && a.EntityId == receiptId.ToString()
            && a.Action == AuditActions.Exported && a.UserId == ownerId))).Should().Be(1);

        var reversed = await owner.PostAsJsonAsync($"{Receipts}/{receiptId}/reverse", new { reason = "Nota errada", version = Version(receipt) });
        reversed.StatusCode.Should().Be(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());
        (await ReceiptPdfAsync(owner, receiptId)).Should().Contain("CANCELADO").And.Contain("Recebimentoestornado:Notaerrada");
        (await owner.GetAsync($"{Receipts}/{Guid.CreateVersion7()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>CA 3 e 9: o modelo do comprovante não tem propriedade de custo, preço nem valor (o PDF não tem de onde tirar).</summary>
    [Fact]
    public void Modelo_do_comprovante_nao_tem_campo_de_custo()
    {
        var names = new[] { typeof(GoodsReceiptPdfModel), typeof(GoodsReceiptPdfLine) }.SelectMany(t => t.GetProperties()).Select(p => p.Name).ToList();
        names.Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal) || n.Contains("Price", StringComparison.Ordinal) || n.Contains("Amount", StringComparison.Ordinal)
            || n.Contains("Freight", StringComparison.Ordinal) || n.Contains("Ipi", StringComparison.Ordinal) || n.Contains("Total", StringComparison.Ordinal));
    }

    /// <summary>EXP-04 CA 15: a planilha dos recebidos usa a busca da tela e traz a mesma contagem da lista, com quem recebeu e o estorno.</summary>
    [Fact]
    public async Task Planilha_dos_recebidos_tem_a_contagem_da_lista()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var (_, receiver) = await ActorAsync("warehouse");
        var item = await ItemAsync();
        var order = await ApprovedOrderAsync(owner, buyer, await SupplierAsync(owner), 0m, 0m, "À vista", null, (item, 2m, 10m, null));
        var receipt = await ReceivedAsync(receiver, order, ReceiptBody(order, null, null, (LineOf(order, item), 2m, null)));
        var number = receipt.GetProperty("number").GetString()!;

        var list = await owner.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Receipts}?q={number}");
        var response = await owner.GetAsync($"{Receipts}/export?format=xlsx&q={number}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet("Recebimentos");
        sheet.Row(1).Cells(1, 8).Select(c => c.GetString()).Should().Equal("Número", "Pedido", "Fornecedor", "Data", "Itens", "Total", "Recebido por", "Estornado");
        (sheet.LastRowUsed()!.RowNumber() - 1).Should().Be(list.GetProperty("total").GetInt32());
        sheet.Cell(2, 1).GetString().Should().Be(number);
        sheet.Cell(2, 6).GetValue<decimal>().Should().Be(20m);
        sheet.Cell(2, 8).GetString().Should().Be("Não");
    }
}
