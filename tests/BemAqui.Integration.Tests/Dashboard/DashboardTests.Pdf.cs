using System.Net;
using BemAqui.Identity.Domain;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>EXP-05 CA 10: fotografia do painel em PDF, só com os blocos que o usuário vê na tela, e a nota com a data e a hora.</summary>
public partial class DashboardTests
{
    private static async Task<string> PanelPdfAsync(HttpClient client)
    {
        var response = await client.GetAsync(Base + "/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Content.Headers.ContentDisposition!.FileName.Should().StartWith("painel-");
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return System.Text.RegularExpressions.Regex.Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), @"\s+", string.Empty);
    }

    [Fact]
    public async Task Fotografia_do_painel_sai_so_com_os_blocos_que_o_usuario_ve()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var full = await PanelPdfAsync(owner);
        full.Should().Contain("Paineldoproprietário").And.Contain("Fotografiade").And.Contain("osnúmerosmudamcomnovoslançamentos")
            .And.Contain("Saldoemcontas").And.Contain("Faturamento").And.Contain("Arecebervencido").And.Contain("Comercial");

        var (_, sales) = await ActorWithAsync("dashboard.view", "quotes.view");
        var partial = await PanelPdfAsync(sales);
        partial.Should().Contain("Comercial").And.Contain("Fotografiade");
        partial.Should().NotContain("Saldoemcontas", "sem finance.view o bloco do caixa não é montado").And.NotContain("Faturamento").And.NotContain("Arecebervencido");

        var (_, nobody) = await ActorWithAsync("quotes.view");
        (await nobody.GetAsync(Base + "/pdf")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
