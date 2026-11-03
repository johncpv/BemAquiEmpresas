using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using FluentAssertions;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Assets;

/// <summary>EXP-05 CA 8 e 17: PDF da OS de manutenção (conteúdo, checklist, custo só com assets.edit, Cancelado) e Bens sem o valor sem assets.edit.</summary>
public partial class AssetTests
{
    private static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", string.Empty);

    private static async Task<string> MaintenancePdfAsync(HttpClient client, Guid orderId, string number)
    {
        var response = await client.GetAsync($"{Orders}/{orderId}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentDisposition!.FileName.Should().Be(number + ".pdf");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return Flat(string.Join(" ", document.GetPages().Select(p => p.Text)));
    }

    [Fact]
    public async Task PDF_da_OS_de_manutencao_traz_o_checklist_as_assinaturas_o_custo_so_para_quem_edita_e_Cancelado()
    {
        var supervisor = await SupervisorAsync();
        var viewer = await ActorAsync("assets:view");
        var site = await SiteAsync(supervisor, Unique("Galpão"));
        var asset = await CreateAsync(supervisor, Input(Unique("Torno"), site: site));
        var assetId = asset.GetProperty("id").GetGuid();
        var plan = await PlanAsync(supervisor, assetId, new { description = "Preventiva trimestral", frequencyType = "Days", frequencyValue = 90, checklist = new[] { "Trocar o óleo", "Revisar correias" } });
        var order = await OrderAsync(supervisor, new { assetId, type = "Preventive", problem = "Preventiva trimestral", planId = plan.GetProperty("id").GetGuid() });
        var orderId = order.GetProperty("id").GetGuid();
        var number = order.GetProperty("number").GetString()!;
        var started = await StartAsync(supervisor, order);
        var item = started.GetProperty("checklist")[0].GetProperty("id").GetGuid();
        await OkAsync(await supervisor.PutAsJsonAsync($"{Orders}/{orderId}/checklist/{item}", new { result = "Done" }));

        var forEditor = await MaintenancePdfAsync(supervisor, orderId, number);
        forEditor.Should().Contain(Flat("OS de manutenção")).And.Contain(number).And.Contain(asset.GetProperty("tag").GetString()).And.Contain(Flat("Preventiva trimestral"))
            .And.Contain(Flat("Trocar o óleo")).And.Contain(Flat("Revisar correias")).And.Contain(Flat("Equipe interna")).And.Contain(Flat("Em andamento"))
            .And.Contain(Flat("Executado por")).And.Contain(Flat("Conferido por")).And.Contain(Flat("Nenhum custo lançado"));
        forEditor.Should().NotContain("CANCELADO");
        var forViewer = await MaintenancePdfAsync(viewer, orderId, number);
        forViewer.Should().Contain(Flat("Trocar o óleo")).And.NotContain(Flat("Nenhum custo lançado"), "o custo só sai para quem tem assets.edit, como a aba Custo");

        var current = await supervisor.GetFromJsonAsync<JsonElement>($"{Orders}/{orderId}");
        await OkAsync(await supervisor.PostAsJsonAsync($"{Orders}/{orderId}/cancel", new { reason = "Peça em falta", version = current.GetProperty("version").GetUInt32() }));
        (await MaintenancePdfAsync(viewer, orderId, number)).Should().Contain("CANCELADO").And.Contain(Flat("Motivo: Peça em falta"));
        (await viewer.GetAsync($"{Orders}/{Guid.NewGuid()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Bens_exportados_omitem_o_valor_de_aquisicao_sem_assets_edit()
    {
        var supervisor = await SupervisorAsync();
        var viewer = await ActorAsync("assets:view");
        var name = Unique("Compressor");
        await CreateAsync(supervisor, Input(name, value: 12_345.67m));

        async Task<IXLWorksheet> SheetAsync(HttpClient client)
        {
            var response = await client.GetAsync($"{Base}/export?format=xlsx&q={Uri.EscapeDataString(name)}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
            return workbook.Worksheet("Bens");
        }

        var full = await SheetAsync(supervisor);
        var headers = full.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        headers.Should().Contain("Valor de aquisição");
        full.Row(2).Cell(headers.IndexOf("Valor de aquisição") + 1).GetValue<decimal>().Should().Be(12_345.67m);

        var limited = await SheetAsync(viewer);
        limited.Row(1).CellsUsed().Select(c => c.GetString()).Should().NotContain("Valor de aquisição", "a coluna é omitida, como na tela").And.Contain("Etiqueta");
        limited.Row(2).Cell(2).GetString().Should().Be(name);
        limited.CellsUsed().Select(c => c.GetString()).Should().NotContain(v => v.Contains("12345", StringComparison.Ordinal) || v.Contains("12.345", StringComparison.Ordinal));
    }
}
