using System.Globalization;
using System.Net;
using System.Text.Json;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Integration.Tests.Persistence;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-01b CA 6 a 9: o resultado gerencial por projeto com o dataset fixo de três meses (M1 = há 2 meses, M2 = mês passado, M3 = este mês),
/// conferido centavo a centavo com a planilha abaixo; a receita somada igual à parte com projeto da Receita bruta do DRE; o drill-down de
/// cada célula com o total igual ao da célula; e o realizado por período somado igual ao realizado do DSH-02.
/// <code>
/// R1 (em andamento)  Receita  M1 30.000,00  M3 45.500,50                         = 75.500,50
///                    Material M1 8.200,00 (baixa)  M2 −700,25 (devolução)        =  7.499,75
///                    Mão de obra M2 3.150,75 · Instalação M2 12.000,00 (título)
///                    Frete M3 980,40 (título pago) · Impostos M3 4.550,05 (nota)
///                    fora: previsto 5.000 (M3), guia 1.500 (M1), compra estocável 2.000 (M1), baixa 999,99 antes de M1
///                    Custos diretos 28.180,95 · Resultado 47.319,55 · Margem 62,7%
/// R2 (entregue)      Receita M2 40.000,00 · Outros M2 41.250,10 (título sem natureza) · Mão de obra M3 500,00
///                    Custos diretos 41.750,10 · Resultado −1.750,10 · Margem −4,4%
/// </code>
/// </summary>
public partial class OwnerPanelTests
{
    private static string M(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Resultado_por_projeto_bate_com_a_planilha_com_o_DRE_e_com_o_drill_down()
    {
        var m3 = new DateOnly(Today.Year, Today.Month, 1);
        var (m1, m2) = (m3.AddMonths(-2), m3.AddMonths(-1));
        var (d1, d2) = (m1.AddDays(9), m2.AddDays(9));
        var dreBefore = await DreGrossAsync(m1, m3);

        var (party, ids) = await SeedAsync(
            new Seed("Resultado R1", ProjectStatus.InProgress, 120_000m, SignedAt: m1.AddMonths(-2), Ledger:
            [
                (CostType.Material, ProjectCostSource.StockIssue, 8_200m, d1), (CostType.Material, ProjectCostSource.StockReturn, -700.25m, d2),
                (CostType.Labor, ProjectCostSource.Labor, 3_150.75m, d2), (CostType.Tax, ProjectCostSource.InvoiceTax, 4_550.05m, m3),
                (CostType.Material, ProjectCostSource.StockIssue, 999.99m, m1.AddDays(-3)),
            ]),
            new Seed("Resultado R2", ProjectStatus.Delivered, 40_000m, ContractStatus.Completed, DeliveredOn: d2, SignedAt: m1.AddMonths(-1),
                Ledger: [(CostType.Labor, ProjectCostSource.Labor, 500m, m3)]));
        var (r1, r2) = (ids["Resultado R1"], ids["Resultado R2"]);
        await ReceivableAsync(party, r1, 30_000m, d1);
        await ReceivableAsync(party, r1, 45_500.50m, m3);
        await ReceivableAsync(party, r2, 40_000m, d2);
        await PayableAsync(party, r1, CostType.Installation, 12_000m, d2);
        await PayableAsync(party, r1, CostType.Freight, 980.40m, m3, PayableStatus.Paid);
        await PayableAsync(party, r1, CostType.Installation, 5_000m, m3, PayableStatus.Planned, DocumentOrigin.Installer);
        await PayableAsync(party, r1, CostType.Tax, 1_500m, d1);
        await PayableAsync(party, r1, CostType.Material, 2_000m, d1, origin: DocumentOrigin.Purchase);
        await PayableAsync(party, r2, null, 41_250.10m, d2, PayableStatus.Paid);
        var owner = await OwnerAsync();
        var period = $"from={M(m1)}&to={M(m3)}";

        // CA 6 e 9: a lista do cliente, pior resultado primeiro, com as somas.
        var list = await OkAsync(owner, $"{Base}/project-results?partyId={party}&{period}");
        list.GetProperty("total").GetInt32().Should().Be(2);
        list.GetProperty("months").EnumerateArray().Select(m => m.GetProperty("month").GetString()).Should().Equal(M(m1), M(m2), M(m3));
        list.GetProperty("provisional").GetBoolean().Should().BeTrue("sem notas reais, a receita é provisória");
        var rows = list.GetProperty("items").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("projectId").GetGuid()).Should().Equal(r2, r1);
        Check(rows[1], 75_500.50m, new() { ["Material"] = 7_499.75m, ["Labor"] = 3_150.75m, ["Installation"] = 12_000m, ["Freight"] = 980.40m, ["Tax"] = 4_550.05m }, 28_180.95m, 47_319.55m, 62.7m);
        Check(rows[0], 40_000m, new() { ["Labor"] = 500m, ["Other"] = 41_250.10m }, 41_750.10m, -1_750.10m, -4.4m);
        var totals = list.GetProperty("totals");
        (totals.GetProperty("revenue").GetDecimal(), totals.GetProperty("directCosts").GetDecimal(), totals.GetProperty("result").GetDecimal())
            .Should().Be((115_500.50m, 69_931.05m, 45_569.45m));

        // CA 9: a receita somada dos projetos = a parte com projeto da Receita bruta do DRE nos mesmos meses (o que estes títulos somaram ao DRE).
        var dreAfter = await DreGrossAsync(m1, m3);
        dreAfter.Zip(dreBefore, (a, b) => a - b).Should().Equal(30_000m, 40_000m, 45_500.50m);

        // CA 7: a visão mês a mês do R1.
        var detail = await OkAsync(owner, $"{Base}/project-results/{r1}?{period}");
        var lines = detail.GetProperty("lines").EnumerateArray().ToDictionary(l => l.GetProperty("key").GetString()!, l => l.GetProperty("values").EnumerateArray().Select(v => v.GetDecimal()).ToList());
        lines["revenue"].Should().Equal(30_000m, 0m, 45_500.50m);
        lines["Material"].Should().Equal(8_200m, -700.25m, 0m);
        lines["Labor"].Should().Equal(0m, 3_150.75m, 0m);
        lines["Installation"].Should().Equal(0m, 12_000m, 0m);
        lines["Freight"].Should().Equal(0m, 0m, 980.40m);
        lines["Tax"].Should().Equal(0m, 0m, 4_550.05m);
        lines["Other"].Should().Equal(0m, 0m, 0m);
        lines["costs"].Should().Equal(8_200m, 14_450.50m, 5_530.45m);
        lines["result"].Should().Equal(21_800m, -14_450.50m, 39_970.05m);
        Dec(detail, "marginPct").Should().Be(62.7m);

        // CA 8: cada célula de receita e de natureza abre os documentos, com o total igual ao da célula (e ao do período, sem mês).
        var months = new[] { m1, m2, m3 };
        foreach (var line in detail.GetProperty("lines").EnumerateArray().Where(l => l.GetProperty("drillable").GetBoolean()))
        {
            var key = line.GetProperty("key").GetString()!;
            var values = line.GetProperty("values").EnumerateArray().Select(v => v.GetDecimal()).ToList();
            for (var i = 0; i < months.Length; i++)
            {
                var cell = await OkAsync(owner, $"{Base}/project-results/{r1}/cell?line={key}&month={M(months[i])}&{period}");
                cell.GetProperty("total").GetDecimal().Should().Be(values[i], $"{key} em {M(months[i])}");
                cell.GetProperty("entries").EnumerateArray().Sum(e => e.GetProperty("amount").GetDecimal()).Should().Be(values[i]);
            }

            (await OkAsync(owner, $"{Base}/project-results/{r1}/cell?line={key}&{period}")).GetProperty("total").GetDecimal().Should().Be(line.GetProperty("total").GetDecimal());
        }

        var material = await OkAsync(owner, $"{Base}/project-results/{r1}/cell?line=Material&month={M(m2)}&{period}");
        material.GetProperty("entries").EnumerateArray().Single().GetProperty("kind").GetString().Should().Be("StockReturn");
        var revenue = await OkAsync(owner, $"{Base}/project-results/{r1}/cell?line=revenue&{period}");
        revenue.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("kind").GetString()).Should().AllBe("Receivable");
        revenue.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("link").GetString()).Should().OnlyContain(l => l!.StartsWith("/app/financeiro/receber/", StringComparison.Ordinal));
        var labor = await OkAsync(owner, $"{Base}/project-results/{r1}/cell?line=Labor&{period}");
        labor.GetProperty("entries").EnumerateArray().Single().GetProperty("document").GetString().Should().NotContain("@", "horas agregadas por OS e centro, sem nome");

        // Validações e 404.
        (await StatusAsync(owner, $"{Base}/project-results?from=2026-13")).Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await StatusAsync(owner, $"{Base}/project-results?from=2020-01&to=2026-01")).Status.Should().Be(HttpStatusCode.UnprocessableEntity, "no máximo 24 meses");
        (await StatusAsync(owner, $"{Base}/project-results?sort=cost")).Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await StatusAsync(owner, $"{Base}/project-results/{r1}/cell?line=nada")).Status.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await StatusAsync(owner, $"{Base}/project-results/{Guid.CreateVersion7()}")).Status.Should().Be(HttpStatusCode.NotFound);

        // O realizado por período somado em todos os meses é o realizado do DSH-02 (um cálculo só).
        await using var scope = await ScopeAsync();
        var service = new BemAqui.Commercial.Application.ProjectMarginService(
            scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>(), scope.ServiceProvider.GetRequiredService<IProjectPayableCosts>(),
            scope.ServiceProvider.GetRequiredService<BemAqui.Parties.Contracts.IParties>(), scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IUserDirectory>(),
            new AllowAllPermissionChecker(), scope.ServiceProvider.GetRequiredService<BemAqui.Production.Contracts.IProjectProductionCosts>(),
            scope.ServiceProvider.GetRequiredService<BemAqui.Production.Contracts.IProductionOrders>(), scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantContext>(),
            scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantScopeFactory>());
        var byPeriod = (await service.RealizedByPeriodAsync([r1, r2], new DateOnly(2000, 1, 1), new DateOnly(2100, 12, 31))).Value;
        foreach (var id in new[] { r1, r2 })
        {
            var figures = (await service.ForProjectAsync(id)).Value.Figures;
            foreach (var l in figures.Lines)
            {
                byPeriod.Where(p => p.ProjectId == id && p.CostType == l.CostType).Sum(p => p.Amount).Should().Be(l.Realized, $"{id}: {l.CostType}");
            }
        }
    }

    private static void Check(JsonElement row, decimal revenue, Dictionary<string, decimal> costs, decimal direct, decimal result, decimal margin)
    {
        row.GetProperty("revenue").GetDecimal().Should().Be(revenue);
        var byType = row.GetProperty("costs").EnumerateArray().ToDictionary(c => c.GetProperty("costType").GetString()!, c => c.GetProperty("amount").GetDecimal());
        foreach (var type in new[] { "Material", "Labor", "Installation", "Freight", "Tax", "Other" })
        {
            byType[type].Should().Be(costs.GetValueOrDefault(type), type);
        }

        (row.GetProperty("directCosts").GetDecimal(), row.GetProperty("result").GetDecimal(), row.GetProperty("marginPct").GetDecimal()).Should().Be((direct, result, margin));
    }

    /// <summary>Linha "Receita bruta" do DRE nos meses (IRevenueSummary.GrossRevenueAsync, a mesma do DRE da FIN-08).</summary>
    private async Task<List<decimal>> DreGrossAsync(DateOnly from, DateOnly to)
    {
        await using var scope = await ScopeAsync();
        var months = await scope.ServiceProvider.GetRequiredService<IRevenueSummary>().GrossRevenueAsync(from, to.AddMonths(1).AddDays(-1));
        return months.Select(m => m.Amount).ToList();
    }
}
