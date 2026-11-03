using System.Net;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Domain;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>EXP-01: exportação de Contas a pagar em xlsx e PDF com os filtros da tela, teto, permissão da lista e auditoria.</summary>
public partial class PayablesTests
{
    private const string Export = "/api/v1/finance/payables/export";

    /// <summary>Texto do PDF sem espaços (a extração junta ou separa palavras conforme a posição).</summary>
    internal static string Flat(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"\s+", string.Empty);

    private async Task<string> ExportDatasetAsync()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (party, _) = await SupplierAsync(owner);
        var tag = "exp" + Guid.NewGuid().ToString("N")[..8];
        foreach (var (description, amount) in new[] { ("Aluguel " + tag, 1_234.56m), ("=1+1 " + tag, 100m) })
        {
            var body = new
            {
                partyId = party, description, accountId = await AccountAsync(), costCenterId = (Guid?)null, amount, issueDate = Iso(Today.AddDays(-5)), dueDate = Iso(Today.AddDays(10)),
                retentionIss = 0m, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, paymentDetails = (object?)null,
            };
            (await owner.SendAsync(Post("/api/v1/finance/payables", body))).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        return tag;
    }

    [Fact]
    public async Task Exportar_em_Excel_traz_as_linhas_do_filtro_tipadas_sem_formula_e_registra_na_auditoria()
    {
        var tag = await ExportDatasetAsync();
        var (viewerId, viewer) = await ActorAsync("view");

        var response = await viewer.GetAsync($"{Export}?format=xlsx&q={tag}&situation=aberto");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be($"contas-a-pagar-{Iso(Today)}.xlsx");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();

        using var workbook = new XLWorkbook(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var sheet = workbook.Worksheet("Contas a pagar");
        sheet.Row(1).Cells(1, 14).Select(c => c.GetString()).Should().Equal(
            "Número", "Fornecedor", "Descrição", "Conta", "Projeto", "Forma", "Emissão", "Vencimento", "Valor", "Retenções", "Líquido", "Pago", "Saldo", "Situação");
        sheet.SheetView.SplitRow.Should().Be(1, "cabeçalho congelado");
        sheet.AutoFilter.IsEnabled.Should().BeTrue();
        var rows = sheet.RowsUsed().Skip(1).ToList();
        rows.Should().HaveCount(2, "só os títulos do filtro, de todas as páginas");

        var rent = rows.Single(r => r.Cell(3).GetString().StartsWith("Aluguel", StringComparison.Ordinal));
        rent.Cell(9).DataType.Should().Be(XLDataType.Number);
        rent.Cell(9).GetValue<decimal>().Should().Be(1_234.56m);
        rent.Cell(9).Style.NumberFormat.Format.Should().Contain("R$");
        rent.Cell(8).DataType.Should().Be(XLDataType.DateTime);
        rent.Cell(8).GetDateTime().Should().Be(Today.AddDays(10).ToDateTime(TimeOnly.MinValue));
        rent.Cell(14).GetString().Should().Be("Em aberto");

        // Anti-injeção (ADR 0045 §2, mesma regra do SafeCsv): o texto continua igual, como texto, nunca fórmula.
        var formula = rows.Single(r => r.Cell(3).GetString().StartsWith('='));
        formula.Cell(3).HasFormula.Should().BeFalse();
        formula.Cell(3).DataType.Should().Be(XLDataType.Text);
        formula.Cell(3).GetString().Should().Be("=1+1 " + tag);
        formula.Cell(3).Style.IncludeQuotePrefix.Should().BeTrue();

        var filters = workbook.Worksheet("Filtros");
        filters.Column(1).CellsUsed().Select(c => c.GetString()).Should().Contain(["Emitido por", "Emitido em", "Busca", "Situação"]);
        filters.Column(2).CellsUsed().Select(c => c.GetString()).Should().Contain([tag, "Em aberto", "Usuário de teste"]);

        // CA 6: quem, quando, formato, linhas e filtros; sem o conteúdo.
        var entries = await QueryAsync(db => db.AuditEntries.Where(a => a.Entity == "Payable" && a.EntityId == "list" && a.Action == AuditActions.Exported && a.UserId == viewerId).ToListAsync());
        var entry = entries.Should().ContainSingle().Subject;
        using var changes = JsonDocument.Parse(entry.Changes);
        changes.RootElement.GetProperty("format").GetProperty("new").GetString().Should().Be("xlsx");
        changes.RootElement.GetProperty("rows").GetProperty("new").GetInt32().Should().Be(2);
        changes.RootElement.GetProperty("filters").GetProperty("new").GetProperty("Busca").GetString().Should().Be(tag);
        entry.Changes.Should().NotContain("Aluguel", "a auditoria não guarda as linhas");
    }

    [Fact]
    public async Task Exportar_em_PDF_traz_a_lista_no_modelo_unico_com_os_filtros()
    {
        var tag = await ExportDatasetAsync();
        var (_, viewer) = await ActorAsync("view");

        var response = await viewer.GetAsync($"{Export}?format=pdf&q={tag}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be($"contas-a-pagar-{Iso(Today)}.pdf");
        using var pdf = PdfDocument.Open(await response.Content.ReadAsByteArrayAsync());
        var text = Flat(string.Join(" ", pdf.GetPages().Select(p => p.Text)));
        text.Should().Contain(Flat("Contas a pagar")).And.Contain("Filtros:").And.Contain(Flat("Busca: " + tag)).And.Contain(Flat("Aluguel " + tag))
            .And.Contain("1.234,56").And.Contain(Flat("Emitido por Usuário de teste")).And.Contain(Flat("Página 1 de 1"));
        text.Should().NotContain("Retenções", "o PDF de lista leva só as colunas principais");
    }

    [Fact]
    public async Task Acima_do_teto_responde_422_sem_gerar_e_formato_desconhecido_tambem()
    {
        var tag = await ExportDatasetAsync();
        var (viewerId, viewer) = await ActorAsync("view");
        await using var limited = tenancy.Factory.WithWebHostBuilder(b => b.UseSetting("Export:MaxXlsxRows", "1"));
        var client = limited.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = viewer.DefaultRequestHeaders.Authorization;

        var tooLarge = await client.GetAsync($"{Export}?format=xlsx&q={tag}");
        tooLarge.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(tooLarge);
        problem.GetProperty("code").GetString().Should().Be("export_too_large");
        problem.GetProperty("detail").GetString().Should().Contain("2 linhas").And.Contain("Refine os filtros");
        problem.GetProperty("limit").GetInt32().Should().Be(1);

        var invalid = await viewer.GetAsync($"{Export}?format=csv&q={tag}");
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(invalid)).GetProperty("code").GetString().Should().Be("export_format_invalid");
        (await QueryAsync(db => db.AuditEntries.CountAsync(a => a.Entity == "Payable" && a.EntityId == "list" && a.UserId == viewerId))).Should().Be(0, "nada foi exportado");
    }

    [Fact]
    public async Task Sem_a_permissao_da_lista_nao_exporta()
    {
        var (_, stranger) = await ActorAsync();
        (await stranger.GetAsync($"{Export}?format=xlsx")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
