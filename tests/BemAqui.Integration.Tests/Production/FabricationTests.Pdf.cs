using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Production.Application;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Production;

public partial class FabricationOrdersTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex PdfSpaces();

    private static async Task<string> FichaAsync(HttpClient client, Guid orderId)
    {
        var response = await client.GetAsync($"{Base}/{orderId}/pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return PdfSpaces().Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), string.Empty);
    }

    /// <summary>
    /// EXP-03 CA 10 a 16: a ficha da OS com as etapas, o checklist, a qualidade, a nota da BOM e o visto; o Proprietário (com
    /// <c>projects.view_cost</c>) baixa e o PDF não traz preço nem custo; aberta sem etapa iniciada leva a faixa; cancelada, "Cancelado" e o motivo.
    /// </summary>
    [Fact]
    public async Task Ficha_da_OS_com_etapas_e_checklist_sem_custo_e_marca_por_situacao()
    {
        var (ownerId, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var center = await CenterAsync("Corte a laser");
        var product = await ProductAsync();
        await RoutingAsync(owner, center, product);
        var (_, _, items) = await ApprovedAsync(owner, [("Equipment", product, 1m)]);
        var order = await GetOrderAsync(owner, (await OrderOfAsync(items[0]))!.Id);
        var id = order.GetProperty("id").GetGuid();

        var text = await FichaAsync(owner, id);
        text.Should().Contain(order.GetProperty("number").GetString()).And.Contain("Ordemdeserviçodefabricação").And.Contain("Corte").And.Contain("Montagem")
            .And.Contain("Conferirprumo").And.Contain("·Qualidade").And.Contain("Etapasaindapodemmudar.").And.Contain(ProductionOrderPdf.NoBom.Replace(" ", string.Empty))
            .And.Contain("Vistodosupervisor").And.Contain("Nenhumdesenhooudocumentoanexado.");
        text.Should().NotContainAny("R$", "1.000,00", "Custo", "custo", "Margem", "Preço", "CANCELADO", "RASCUNHO");
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == ProductionOrderDocumentService.AuditEntity && a.EntityId == id.ToString()
            && a.Action == AuditActions.Exported && a.UserId == ownerId))).Should().Be(1);

        var cancelled = await owner.PostAsJsonAsync($"{Base}/{id}/cancel", new { reason = "Cliente desistiu", version = Version(order) });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());
        (await FichaAsync(owner, id)).Should().Contain("CANCELADO").And.Contain("Motivo:Clientedesistiu").And.NotContain("Etapasaindapodemmudar.");
        (await owner.GetAsync($"{Base}/{Guid.CreateVersion7()}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>CA 14: o modelo da ficha não tem propriedade de custo, preço nem margem (o PDF não tem de onde tirar).</summary>
    [Fact]
    public void Modelo_da_ficha_nao_tem_campo_de_custo()
    {
        var names = new[] { typeof(ProductionOrderPdfModel), typeof(ProductionOrderPdfStep), typeof(ProductionOrderPdfMaterial), typeof(ProductionOrderPdfMaterialLine), typeof(ProductionOrderPdfFile) }
            .SelectMany(t => t.GetProperties()).Select(p => p.Name).ToList();
        names.Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal) || n.Contains("Price", StringComparison.Ordinal) || n.Contains("Margin", StringComparison.Ordinal)
            || n.Contains("Rate", StringComparison.Ordinal) || n.Contains("Amount", StringComparison.Ordinal));
        ProductionOrderPdf.Mark(BemAqui.Production.Domain.ProductionOrderStatus.InProgress, true, null).Should().Be((BemAqui.BuildingBlocks.Pdf.PdfMark.None, (string?)null));
    }
}
