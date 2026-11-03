using System.Net;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Domain;
using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex InstallationPdfSpaces();

    private static async Task<string> InstallationPdfTextAsync(HttpClient client, string url, string fileName)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be(fileName);
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return InstallationPdfSpaces().Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), string.Empty);
    }

    /// <summary>
    /// EXP-04 CA 7 e 8: a OS de instalação impressa sem o valor do instalador; o termo de aceite em branco com o texto fixo e "Série: ____"
    /// (sem romaneio); concluída, o termo sai com quem aceitou e a faixa; cancelada, a OS sai com "Cancelado" e o termo é recusado (409).
    /// </summary>
    [Fact]
    public async Task OS_de_instalacao_e_termo_de_aceite_em_PDF_sem_valor_do_instalador()
    {
        var (ownerId, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false, milestones: AcceptanceMilestones);
        var (_, projectId) = await ApproveAsync(owner, id, quote, Today.AddDays(-5));
        var installer = await CustomerAsync(owner, "Installer");
        var created = await owner.SendAsync(Post(Installations, new
        {
            projectId, projectItemIds = (Guid[]?)null, installerId = installer, contractValue = 4_321.99m, plannedStart = Iso(Today), plannedEnd = Iso(Today.AddDays(5)),
        }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var os = await JsonAsync(created);
        var osId = os.GetProperty("id").GetGuid();
        var number = os.GetProperty("number").GetString()!;

        var pdf = await InstallationPdfTextAsync(owner, $"{Installations}/{osId}/pdf", number + ".pdf");
        pdf.Should().Contain(number).And.Contain("Ordemdeserviçodeinstalação").And.Contain("Preparaçãodaobra").And.Contain("Checklistdaentregatécnica");
        pdf.Should().NotContainAny("4.321,99", "R$", "CANCELADO");
        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == InstallationDocumentService.AuditEntity && a.EntityId == osId.ToString()
            && a.Action == AuditActions.Exported && a.UserId == ownerId))).Should().Be(1);

        var blank = await InstallationPdfTextAsync(owner, $"{Installations}/{osId}/acceptance-term/pdf", number + "-termo-de-aceite.pdf");
        blank.Should().Contain("Termodeaceitetécnico").And.Contain(InstallationPdf.Declaration.Replace(" ", string.Empty)).And.Contain("Série:____")
            .And.Contain("Conforme").And.Contain("Nãoconforme").And.Contain("Observação").And.NotContain("Aceiteregistrado").And.NotContain("4.321,99");

        await WithDbAsync(db => db.Set<ProductionOrder>().Where(o => o.Id == osId).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, ProductionOrderStatus.Completed).SetProperty(o => o.AcceptedOn, Today).SetProperty(o => o.AcceptedByName, "Carlos Síndico")));
        var filled = await InstallationPdfTextAsync(owner, $"{Installations}/{osId}/acceptance-term/pdf", number + "-termo-de-aceite.pdf");
        filled.Should().Contain("Aceiteregistradoem" + Today.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)).And.Contain("CarlosSíndico");

        await WithDbAsync(db => db.Set<ProductionOrder>().Where(o => o.Id == osId).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, ProductionOrderStatus.Cancelled).SetProperty(o => o.CancelReason, "Obra embargada")));
        (await InstallationPdfTextAsync(owner, $"{Installations}/{osId}/pdf", number + ".pdf")).Should().Contain("CANCELADO").And.Contain("Motivo:Obraembargada");
        (await owner.GetAsync($"{Installations}/{osId}/acceptance-term/pdf")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.GetAsync($"{Installations}/{Guid.CreateVersion7()}/acceptance-term/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
