using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>EXP-05 CA 2 e 7: ficha do título a receber em PDF (dados, baixas, saldo, Cancelado com o motivo), IDOR e auditoria.</summary>
public partial class ReceivablesTests
{
    private static async Task<string> PdfTextAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return Flat(string.Join(" ", document.GetPages().Select(p => p.Text)));
    }

    [Fact]
    public async Task Ficha_do_titulo_a_receber_sai_com_as_baixas_o_saldo_e_Cancelado_com_o_motivo_e_id_de_outro_recurso_da_404()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var bank = await BankAsync(owner);
        var receivable = await ReceivableAsync(owner, party, 1_000m);
        var id = receivable.GetProperty("id").GetGuid();
        var number = receivable.GetProperty("number").GetString()!;
        var settled = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 400m, Version(receivable), interest: 12.34m));
        settled.StatusCode.Should().Be(HttpStatusCode.OK, await settled.Content.ReadAsStringAsync());
        var (viewerId, viewer) = await ActorAsync("view");

        var response = await viewer.GetAsync($"/api/v1/finance/receivables/{id}/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be(number + ".pdf");
        var text = await PdfTextAsync(response);
        text.Should().Contain(Flat("Título a receber")).And.Contain(number).And.Contain(Flat("Venda de teste")).And.Contain("1.000,00").And.Contain("400,00").And.Contain("12,34")
            .And.Contain(Flat("Saldo")).And.Contain("600,00").And.Contain(Flat("Sem boleto emitido")).And.Contain(Flat("Emitido por"));
        text.Should().NotContain("CANCELADO");

        var current = await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{id}"));
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements/{(await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{id}/settlements")))[0].GetProperty("id").GetGuid()}/reverse",
            new { reason = "Lançado errado", version = Version(current) })).StatusCode.Should().Be(HttpStatusCode.OK);
        current = await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{id}"));
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/cancel", new { reason = "Cliente desistiu da compra", version = Version(current) })).StatusCode.Should().Be(HttpStatusCode.OK);
        var cancelled = await PdfTextAsync(await viewer.GetAsync($"/api/v1/finance/receivables/{id}/pdf"));
        cancelled.Should().Contain("CANCELADO").And.Contain(Flat("Cliente desistiu da compra")).And.Contain("Estornada").And.Contain(Flat("Lançado errado"));

        // IDOR: id inexistente e id de um título a pagar na rota do a receber (e vice-versa) dão 404, sem PDF.
        (await viewer.GetAsync($"/api/v1/finance/receivables/{Guid.NewGuid()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await viewer.GetAsync($"/api/v1/finance/payables/{id}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await QueryAsync(db => db.AuditEntries.CountAsync(a => a.Entity == "Receivable" && a.EntityId == id.ToString() && a.Action == AuditActions.Exported && a.UserId == viewerId)))
            .Should().Be(2, "cada PDF gerado entra na auditoria de exportação");
    }
}
