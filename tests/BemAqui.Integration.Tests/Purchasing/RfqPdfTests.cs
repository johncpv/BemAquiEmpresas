using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Purchasing.Application.Documents;
using BemAqui.Purchasing.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Purchasing;

/// <summary>
/// EXP-03 CA 1 a 3 e 16 com PostgreSQL real: o pedido de cotação de um fornecedor nunca traz preço, nome ou resposta de outro; o fornecedor
/// de outra cotação dá 404 (IDOR); a cotação fechada recusa o pedido de cotação; o mapa sai como "Prévia" aberto e "Cancelado" cancelado;
/// cada download fica na auditoria. Banco desta classe; cada teste cria os próprios fornecedores, itens e cotações.
/// </summary>
public partial class RfqPdfTests(TenancyFixture tenancy) : RfqTestBase(tenancy), IClassFixture<TenancyFixture>
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private static string Flat(string text) => Spaces().Replace(text, string.Empty);

    private static async Task<string> PdfTextAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return Flat(string.Join(" ", document.GetPages().Select(p => p.Text)));
    }

    private static string NameOf(JsonElement rfq, Guid party) => SupplierOf(rfq, party).GetProperty("partyName").GetString()!;

    [Fact]
    public async Task Pedido_de_cotacao_so_do_fornecedor_sem_preco_de_outro_IDOR_e_mapa_por_situacao()
    {
        var (_, owner) = await ActorAsync("owner");
        var (buyerId, buyer) = await ActorAsync(Buyer);
        var first = await SupplierAsync(owner);
        var second = await SupplierAsync(owner);
        var item = await ItemAsync();
        var rfq = await CreateTypedRfqAsync(buyer, [(item, 12m)], [first, second]);
        var other = await CreateTypedRfqAsync(buyer, [(item, 1m)], [second]);
        var line = LineOf(rfq, item);
        rfq = await RespondAsync(buyer, rfq, SupplierRow(rfq, second), [new Quote(line, 77.77m)], freight: 31.31m);

        // CA 1: o pedido de cotação do primeiro fornecedor não traz o preço, o frete nem o nome do segundo.
        var request = await PdfTextAsync(await buyer.GetAsync($"{Rfqs}/{Id(rfq)}/suppliers/{SupplierRow(rfq, first)}/pdf"));
        request.Should().Contain(Flat(rfq.GetProperty("number").GetString()!)).And.Contain(Flat(NameOf(rfq, first))).And.Contain("Pedidodecotação").And.Contain("Respostapor")
            .And.Contain("Preçounit.").And.Contain("12");
        request.Should().NotContain(Flat(NameOf(rfq, second))).And.NotContain("77,77").And.NotContain("31,31").And.NotContain("Últimacompra");

        // CA 16: fornecedor de outra cotação (IDOR) dá 404; o download fica na auditoria de exportação.
        (await buyer.GetAsync($"{Rfqs}/{Id(rfq)}/suppliers/{SupplierRow(other, second)}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == RfqDocumentService.AuditEntity && a.EntityId == Id(rfq).ToString()
            && a.Action == AuditActions.Exported && a.UserId == buyerId))).Should().Be(1);

        // CA 2 e 3: aberta com resposta, o mapa sai como Prévia, com o menor custo e o sugerido; cancelada, com Cancelado.
        var open = await PdfTextAsync(await buyer.GetAsync($"{Rfqs}/{Id(rfq)}/comparison/pdf"));
        open.Should().Contain("PRÉVIA").And.Contain("Menorcusto").And.Contain("Sugerido").And.Contain("77,77").And.Contain("Comprador").And.Contain("Aprovação");
        var cancelled = await buyer.PostAsJsonAsync($"{Rfqs}/{Id(rfq)}/cancel", new { reason = "Compra adiada", version = Version(rfq) });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await PdfTextAsync(await buyer.GetAsync($"{Rfqs}/{Id(rfq)}/comparison/pdf"))).Should().Contain("CANCELADO").And.Contain("Compraadiada").And.NotContain("PRÉVIA");

        var closed = await buyer.GetAsync($"{Rfqs}/{Id(rfq)}/suppliers/{SupplierRow(rfq, first)}/pdf");
        closed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await closed.Content.ReadAsStringAsync()).Should().Contain("A cotação não está aberta.");
    }

    [Fact]
    public async Task Mapa_da_cotacao_aberta_sem_resposta_e_recusado()
    {
        var (_, owner) = await ActorAsync("owner");
        var (_, buyer) = await ActorAsync(Buyer);
        var rfq = await CreateTypedRfqAsync(buyer, [(await ItemAsync(), 1m)], [await SupplierAsync(owner)]);
        (await buyer.GetAsync($"{Rfqs}/{Id(rfq)}/comparison/pdf")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Sem banco: o modelo do pedido de cotação não tem campo de preço nem de resposta; marcas e nome do arquivo.</summary>
    [Fact]
    public void Modelo_do_pedido_de_cotacao_sem_preco_marcas_e_nome_do_arquivo()
    {
        typeof(RfqRequestPdfModel).GetProperties().Select(p => p.Name).Concat(typeof(RfqRequestPdfLine).GetProperties().Select(p => p.Name))
            .Should().NotContain(n => n.Contains("Price", StringComparison.Ordinal) || n.Contains("Cost", StringComparison.Ordinal) || n.Contains("Total", StringComparison.Ordinal)
                || n.Contains("Last", StringComparison.Ordinal) || n.Contains("Freight", StringComparison.Ordinal));
        RfqPdf.ComparisonMark(RfqStatus.Open).Should().Be(PdfMark.Preview);
        RfqPdf.ComparisonMark(RfqStatus.Closed).Should().Be(PdfMark.None);
        RfqPdf.ComparisonMark(RfqStatus.Cancelled).Should().Be(PdfMark.Cancelled);
        RfqPdf.RequestFileName("COT-2026-0003", "Gama Aços Especiais Ltda").Should().Be("COT-2026-0003-gama-acos.pdf");
        RfqPdf.ComparisonFileName("COT-2026-0003").Should().Be("COT-2026-0003-comparativo.pdf");
    }
}
