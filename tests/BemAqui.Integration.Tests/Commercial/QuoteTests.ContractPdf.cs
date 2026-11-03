using System.Net;
using System.Text.RegularExpressions;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Commercial.Application.Documents;
using BemAqui.Identity.Domain;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex PdfSpaces();

    private static async Task<string> PdfTextAsync(HttpClient client, string url, string fileName)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentDisposition!.FileName!.Trim('"').Should().Be(fileName);
        using var document = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        return PdfSpaces().Replace(string.Join(" ", document.GetPages().Select(p => p.Text)), string.Empty);
    }

    /// <summary>
    /// EXP-04 CA 2 a 6: o resumo do contrato com itens, marcos e assinaturas, sem custo nem margem (para quem vê custo e para quem não vê);
    /// cancelado com a marca e o motivo; o aditivo em rascunho com "Rascunho"; aditivo de outro contrato é 404 (IDOR); o download é auditado.
    /// </summary>
    [Fact]
    public async Task PDF_do_contrato_e_do_aditivo_sem_custo_com_marca_por_situacao_e_IDOR()
    {
        var (ownerId, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var (_, engineering) = await ActorAsync("engineering");
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false);
        var (contractId, _) = await ApproveAsync(owner, id, quote, Today);
        var contract = await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"));
        var number = contract.GetProperty("number").GetString()!;

        foreach (var client in new[] { owner, engineering })
        {
            var text = await PdfTextAsync(client, $"/api/v1/contracts/{contractId}/pdf", number + ".pdf");
            text.Should().Contain(number).And.Contain("Elevador6paradas").And.Contain("Marcosdepagamento").And.Contain("Sinal").And.Contain("Contratante").And.Contain("Contratada");
            text.Should().NotContainAny("Custo", "custo", "Margem", "margem", "CANCELADO", "RASCUNHO");
        }

        (await WithDbAsync(db => db.AuditEntries.CountAsync(a => a.Entity == ContractDocumentService.ContractEntity && a.EntityId == contractId.ToString()
            && a.Action == AuditActions.Exported && a.UserId == ownerId))).Should().Be(1);

        var path = $"/api/v1/contracts/{contractId}/amendments";
        var created = await owner.SendAsync(Post(path, new
        {
            description = "Mais uma parada", valueDelta = 10_000m, daysDelta = 15, milestones = new[] { Milestone("Sinal do aditivo", 100m) }, reductions = (object[]?)null,
            budgetDeltas = (object?)null, version = (uint?)null,
        }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var amendmentId = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var amendment = await PdfTextAsync(engineering, $"{path}/{amendmentId}/pdf", $"{number}-aditivo-1.pdf");
        amendment.Should().Contain("RASCUNHO").And.Contain("Maisumaparada").And.Contain("Sinaldoaditivo").And.Contain("10.000,00").And.NotContainAny("Custo", "Orçado", "Material");

        var (otherQuote, other, _) = await SentQuoteAsync(owner, canary: false);
        var (otherContract, _) = await ApproveAsync(owner, otherQuote, other, Today);
        (await owner.GetAsync($"/api/v1/contracts/{otherContract}/amendments/{amendmentId}/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound, "aditivo de outro contrato (IDOR)");

        await WithDbAsync(db => db.Set<BemAqui.Commercial.Domain.Contract>().Where(c => c.Id == contractId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, BemAqui.Commercial.Domain.ContractStatus.Cancelled).SetProperty(c => c.CancelReason, "Cliente desistiu")));
        (await PdfTextAsync(owner, $"/api/v1/contracts/{contractId}/pdf", number + ".pdf")).Should().Contain("CANCELADO").And.Contain("Motivo:Clientedesistiu");
    }

    /// <summary>EXP-04 CA 15 e 16: a planilha de projetos tem a margem orçada e o % recebido só para quem tem a ação; sem ela, as colunas somem.</summary>
    [Fact]
    public async Task Planilha_de_projetos_omite_margem_e_recebido_sem_a_acao()
    {
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var (_, engineering) = await ActorAsync("engineering");
        var (id, quote, _) = await SentQuoteAsync(owner, canary: false);
        var (_, projectId) = await ApproveAsync(owner, id, quote, Today);
        var project = (await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("number").GetString()!;

        async Task<IReadOnlyList<string>> HeadersAsync(HttpClient client)
        {
            var response = await client.GetAsync($"/api/v1/projects/export?format=xlsx&q={project}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            response.Content.Headers.ContentDisposition!.FileName.Should().Be($"projetos-{Iso(Today)}.xlsx");
            using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
            var sheet = workbook.Worksheet("Projetos");
            sheet.RowsUsed().Count().Should().Be(2, "o cabeçalho e o projeto do filtro");
            sheet.Cell(2, 1).GetString().Should().Be(project);
            return sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
        }

        (await HeadersAsync(owner)).Should().Contain(["Margem orçada", "% recebido"]);
        (await HeadersAsync(engineering)).Should().NotContain("Margem orçada").And.NotContain("% recebido").And.Contain("Contratado");
    }

    /// <summary>EXP-04 CA 3: o modelo do contrato e do aditivo não tem propriedade de custo, margem nem orçado.</summary>
    [Fact]
    public void Modelo_do_contrato_e_do_aditivo_nao_tem_custo()
    {
        var names = new[] { typeof(ContractPdfModel), typeof(ContractPdfItem), typeof(ContractPdfMilestone), typeof(ContractPdfAmendment), typeof(AmendmentPdfModel), typeof(AmendmentPdfReduction) }
            .SelectMany(t => t.GetProperties()).Select(p => p.Name).ToList();
        names.Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal) || n.Contains("Margin", StringComparison.Ordinal) || n.Contains("Budget", StringComparison.Ordinal));
        ContractPdf.Mark(BemAqui.Commercial.Domain.AmendmentStatus.Approved, null).Should().Be((BemAqui.BuildingBlocks.Pdf.PdfMark.None, (string?)null));
        ContractPdf.Mark(BemAqui.Commercial.Domain.AmendmentStatus.Rejected, "Caro").Mark.Should().Be(BemAqui.BuildingBlocks.Pdf.PdfMark.Cancelled);
        ContractPdf.Mark(BemAqui.Commercial.Domain.ContractStatus.Active, true, null).Notice.Should().Be(ContractPdf.OnboardingNotice);
    }
}
