using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Production.Application;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Production;

public abstract partial class FabricationTestBase
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Exp05Spaces();

    /// <summary>Texto do PDF sem espaços (a extração junta ou separa palavras conforme a posição).</summary>
    protected static async Task<string> PdfTextAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return Exp05Spaces().Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), string.Empty);
    }

    protected static string Flat(string text) => Exp05Spaces().Replace(text, string.Empty);
}

/// <summary>EXP-05 CA 3 e 4: PDF da BOM (Rascunho, liberada, substituída; sem custo) e do roteiro (etapas, checklist; inativo com a faixa).</summary>
public partial class FabricationOrdersTests
{
    [Fact]
    public async Task PDF_da_BOM_por_revisao_sem_custo_e_do_roteiro_com_checklist()
    {
        var (ownerId, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var (_, order, _, _) = await BomOrderAsync();
        var steel = await MaterialAsync("Chapa de aço 3 mm");
        var released = await ReleasedBomAsync(owner, order, BomLine(steel, 1.5m, notes: "Cortar no laser"));
        var rev0 = released.GetProperty("id").GetGuid();
        var number = await WithDbAsync(db => db.ProductionOrders.AsNoTracking().Where(o => o.Id == order).Select(o => o.Number).SingleAsync());

        var text = await PdfTextAsync(owner, $"{Boms}/{rev0}/pdf");
        text.Should().Contain(Flat("Lista de materiais (BOM)")).And.Contain(number).And.Contain("rev.0").And.Contain(Flat("Chapa de aço 3 mm")).And.Contain(Flat("Cortar no laser"))
            .And.Contain("Liberada").And.Contain(Flat("Conferido por"));
        text.Should().NotContainAny("RASCUNHO", "R$", "Custo", "custo", "substituída");
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == "Bom" && a.EntityId == rev0.ToString() && a.Action == AuditActions.Exported && a.UserId == ownerId)))
            .Should().Be(1);

        var revised = await JsonAsync(await owner.SendAsync(Post($"{Boms}/{rev0}/revise", new { version = Version(released) })));
        var rev1 = revised.GetProperty("id").GetGuid();
        (await PdfTextAsync(owner, $"{Boms}/{rev1}/pdf")).Should().Contain("RASCUNHO").And.Contain("rev.1");
        await ReleaseAsync(owner, revised);
        (await PdfTextAsync(owner, $"{Boms}/{rev0}/pdf")).Should().Contain(Flat("Revisão substituída pela 1")).And.NotContain("RASCUNHO");
        (await owner.GetAsync($"{Boms}/{Guid.CreateVersion7()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var center = await CenterAsync("Solda", "Linha 3");
        var routing = await RoutingAsync(owner, center, null, "Roteiro PDF", Step("Corte", "C", center, 2m), Step("Montagem", "M", center, 3m, checklist: ["Conferir prumo"]),
            Step("Qualidade", "Q", center, 0.5m, quality: true));
        var routingText = await PdfTextAsync(owner, $"{Routings}/{routing}/pdf");
        routingText.Should().Contain(Flat("Roteiro de fabricação")).And.Contain(Flat("Roteiro PDF")).And.Contain("Montagem").And.Contain(Flat("Conferir prumo"))
            .And.Contain("·Qualidade").And.Contain("Solda").And.NotContain(Flat("Roteiro inativo"));
        (await owner.PostAsync($"{Routings}/{routing}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PdfTextAsync(owner, $"{Routings}/{routing}/pdf")).Should().Contain(Flat("Roteiro inativo"));
    }
}

/// <summary>EXP-05 CA 9: PDF da inspeção de qualidade com production.view: "Prévia" enquanto não decidida; reprovada com a NC e o "Não conforme".</summary>
public partial class FabricationBoardTests
{
    [Fact]
    public async Task PDF_da_inspecao_previa_ate_decidir_e_reprovada_com_a_NC()
    {
        var (_, center, order) = await QualityDatasetAsync();
        var (_, _, operatorClient, _) = await OperatorAsync(center);
        await WorkUntilQualityAsync(operatorClient, order, 1, 2);
        var q = await StepIdAsync(order, 3);
        var (_, viewer) = await ActorAsync("production:view");
        var url = $"{Quality}/{q}/pdf";

        var preview = await PdfTextAsync(viewer, url);
        preview.Should().Contain(Flat("Inspeção de qualidade")).And.Contain("PRÉVIA").And.Contain(Flat(QualityInspectionPdf.NotDecidedNotice)).And.Contain(Flat("Conferir nivelamento"))
            .And.Contain(Flat("Ruído do motor")).And.Contain(Flat("Não decidida")).And.Contain("tentativa1");

        var (_, inspector) = await ActorAsync("production:view,approve_quality");
        var detail = await QualityStepAsync(inspector, q);
        (await inspector.PutAsJsonAsync($"{Quality}/steps/{q}/checklist/{detail.GetProperty("checklist")[0].GetProperty("id").GetGuid()}", new { result = "NonConforming" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await OkAsync(await RejectQualityAsync(inspector, q, await OrderVersionAsync(order)));
        var nc = rejected.GetProperty("nonConformityNumber").GetString()!;

        var decided = await PdfTextAsync(viewer, url + "?attempt=1");
        decided.Should().Contain("Reprovada").And.Contain(nc).And.Contain(Flat("Não conforme")).And.Contain(Flat("Nenhuma foto anexada.")).And.NotContain("PRÉVIA");
        decided.Should().NotContainAny("R$", "Custo");
        (await viewer.GetAsync(url + "?attempt=9")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await viewer.GetAsync($"{Quality}/{Guid.CreateVersion7()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
