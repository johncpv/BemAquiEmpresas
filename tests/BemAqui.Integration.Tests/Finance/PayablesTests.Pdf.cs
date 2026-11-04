using System.Net;
using BemAqui.Identity.Domain;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>EXP-05 CA 7: ficha do título a pagar em PDF, com as retenções, o líquido e a ordem de pagamento.</summary>
public partial class PayablesTests
{
    [Fact]
    public async Task Ficha_do_titulo_a_pagar_sai_com_as_retencoes_e_o_liquido()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (party, _) = await SupplierAsync(owner);
        var item = (await PayableAsync(owner, party, 1_000m, iss: 50m)).GetProperty("items")[0];
        var (_, viewer) = await ActorAsync("view");

        var response = await viewer.GetAsync($"/api/v1/finance/payables/{item.GetProperty("id").GetGuid()}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentDisposition!.FileName.Should().Be(item.GetProperty("number").GetString() + ".pdf");
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var text = PayablesTests.Flat(string.Join(" ", document.GetPages().Select(p => p.Text)));
        text.Should().Contain(Flat("Título a pagar")).And.Contain(Flat("ISS retido")).And.Contain("50,00").And.Contain(Flat("Líquido")).And.Contain("950,00")
            .And.Contain(Flat("Serviço de teste")).And.Contain(Flat("O título ainda não entrou numa ordem de pagamento"));
        text.Should().NotContain("CANCELADO");
    }
}
