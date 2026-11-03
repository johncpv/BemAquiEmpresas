using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Purchasing;

public partial class PurchaseRequestTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex PdfSpaces();

    private static async Task<string> RequestPdfAsync(HttpClient client, Guid id)
    {
        var response = await client.GetAsync($"{Base}/purchase-requests/{id}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".pdf");
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return PdfSpaces().Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), string.Empty);
    }

    /// <summary>EXP-05 CA 6: o PDF da SC com a origem, o destino, os itens com a situação da linha e os vínculos; cancelada com "Cancelado" e o motivo.</summary>
    [Fact]
    public async Task PDF_da_solicitacao_com_itens_e_situacao_e_cancelado()
    {
        await SetReleasedAsync(true);
        var (_, client) = await ActorAsync(Buyer);
        var item = await ItemAsync();
        var created = await JsonAsync(await client.SendAsync(Post(Base + "/purchase-requests", ManualBody(null, item, 7m))));
        var id = created.GetProperty("id").GetGuid();
        var number = created.GetProperty("number").GetString()!;

        var text = await RequestPdfAsync(client, id);
        text.Should().Contain("Solicitaçãodecompra").And.Contain(number).And.Contain("Estoquegeral").And.Contain("Manual").And.Contain("Aberta")
            .And.Contain(PdfSpaces().Replace("Nenhuma cotação ou pedido atende esta solicitação ainda.", string.Empty)).And.NotContain("CANCELADO").And.NotContain("R$");

        var cancelled = await client.PostAsJsonAsync($"{Base}/purchase-requests/{id}/cancel", new { reason = "Compra desistida", version = created.GetProperty("version").GetUInt32() });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await RequestPdfAsync(client, id)).Should().Contain("CANCELADO").And.Contain("Motivo:Compradesistida").And.Contain("Cancelada");
        (await client.GetAsync($"{Base}/purchase-requests/{Guid.CreateVersion7()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
