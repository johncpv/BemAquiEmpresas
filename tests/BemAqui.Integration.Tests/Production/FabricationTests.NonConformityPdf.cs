using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Production;

public partial class FabricationBoardTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex NcPdfSpaces();

    private static async Task<string> NcPdfAsync(HttpClient client, Guid nc)
    {
        var response = await client.GetAsync($"{Ncs}/{nc}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return NcPdfSpaces().Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), string.Empty);
    }

    private static async Task<IReadOnlyList<string>> NcExportHeadersAsync(HttpClient client)
    {
        var response = await client.GetAsync($"{Ncs}/export?format=xlsx&filter=todas");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        return workbook.Worksheet("Não conformidades").Row(1).CellsUsed().Select(c => c.GetString()).ToList();
    }

    /// <summary>
    /// EXP-04 CA 3, 14 e 16: o PDF da NC traz o checklist, a faixa "Em aberto" e o custo estimado só para quem tem <c>projects.view_cost</c>; na
    /// lista exportada, a coluna "Custo estimado" some sem a ação (não vem vazia).
    /// </summary>
    [Fact]
    public async Task PDF_e_planilha_da_NC_com_custo_so_para_quem_tem_view_cost()
    {
        var (_, center, order) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        var (_, inspector) = await ActorAsync("production:view,approve_quality");
        await MarkQualityAsync(inspector, q, "NonConforming");
        var nc = (await OkAsync(await RejectQualityAsync(inspector, q, await OrderVersionAsync(order)))).GetProperty("nonConformityId").GetGuid();
        var (_, costEditor) = await ActorAsync("production:view,edit;projects:view,view_cost");
        var detail = await OkAsync(await costEditor.GetAsync($"{Ncs}/{nc}"));
        await OkAsync(await costEditor.PutAsJsonAsync($"{Ncs}/{nc}/cost", new { cost = 350.46m, version = Version(detail) }));

        var (_, viewer) = await ActorAsync("production:view");
        var plain = await NcPdfAsync(viewer, nc);
        plain.Should().Contain(detail.GetProperty("number").GetString()).And.Contain("Emaberto").And.Contain("Painelriscadonalateral").And.Contain("Conferirnivelamento")
            .And.Contain("Nãoconforme");
        plain.Should().NotContainAny("350,46", "Custoestimado");
        (await NcPdfAsync(costEditor, nc)).Should().Contain("Custoestimado").And.Contain("350,46");

        (await NcExportHeadersAsync(viewer)).Should().NotContain("Custo estimado").And.Contain("Idade (dias)");
        (await NcExportHeadersAsync(costEditor)).Should().Contain("Custo estimado");
        (await viewer.GetAsync($"{Ncs}/{Guid.CreateVersion7()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
