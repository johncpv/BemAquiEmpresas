using System.Net;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Pdf;
using BemAqui.Commercial.Application.Documents;
using BemAqui.Commercial.Domain;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex ProjectPdfSpaces();

    private static async Task<string> ProjectPdfAsync(HttpClient client, Guid projectId)
    {
        var response = await client.GetAsync($"/api/v1/projects/{projectId}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return ProjectPdfSpaces().Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), string.Empty);
    }

    /// <summary>
    /// EXP-05 CA 5: o resumo do projeto sai com os dados, os itens e o cronograma dos marcos para quem vê o projeto; o bloco Custos só com
    /// <c>projects.view_cost</c> e o Financeiro só com <c>finance.view</c> (a Engenharia baixa sem os dois); id de outro projeto, 404.
    /// </summary>
    [Fact]
    public async Task Resumo_do_projeto_em_PDF_com_custos_e_financeiro_so_com_a_permissao()
    {
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false);
        var (_, projectId) = await ApproveAsync(owner, id, quote, Today);
        var number = (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("number").GetString()!;

        var full = await ProjectPdfAsync(owner, projectId);
        full.Should().Contain("Resumodoprojeto").And.Contain(number).And.Contain("Itens").And.Contain("Cronograma(marcosdocontrato)").And.Contain("Custos")
            .And.Contain("Margemorçada").And.Contain("Financeiro").And.Contain("%recebidodocontratado").And.NotContain("CANCELADO");

        var (_, engineering) = await ActorAsync("engineering");
        var limited = await ProjectPdfAsync(engineering, projectId);
        limited.Should().Contain(number).And.Contain("Cronograma(marcosdocontrato)").And.NotContain("Margemorçada").And.NotContain("Orçado").And.NotContain("%recebidodocontratado")
            .And.NotContain("Areceber");
        (await owner.GetAsync($"/api/v1/projects/{Guid.CreateVersion7()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public void Resumo_do_projeto_cancelado_leva_a_marca_e_a_etapa_segue_P_E_F_I_T_C()
    {
        ProjectSummaryPdf.Mark(ProjectStatus.Cancelled).Mark.Should().Be(PdfMark.Cancelled);
        ProjectSummaryPdf.Mark(ProjectStatus.InProgress).Mark.Should().Be(PdfMark.None);
        ProjectSummaryPdf.StageIndex(ProjectStatus.Installing).Should().Be(3);
        ProjectSummaryPdf.StageIndex(ProjectStatus.Cancelled).Should().Be(-1);
    }
}
