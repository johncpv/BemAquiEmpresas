using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// DSH-02 CA 2 a 7 e 10 (ADR 0025 §1 e adendo D8), sem banco: realizado por fonte, comprometido de material por saldo (positivo, zero, nunca
/// negativo), previsto no comprometido, guia de imposto fora, imposto orçado e com nota, projetado, desvio, margens com receita 0 e consolidado
/// ponderado pela receita. O dataset fixo dos 4 projetos (<see cref="MarginDataset"/>) é a "planilha" do teste de integração.
/// </summary>
public class ProjectMarginCalculatorTests
{
    private static MarginLine LineOf(MarginResult r, CostType type) => r.Lines.Single(l => l.CostType == type);

    private static PayableCostTotals Pay(CostType type, decimal confirmed = 0, decimal planned = 0, decimal purchase = 0, decimal guide = 0) => new(type, confirmed, planned, purchase, guide);

    private static LedgerCostTotals Ledger(CostType type, ProjectCostSource source, decimal amount, decimal invoice = 0, bool backfill = false) => new(type, source, backfill, amount, invoice);

    private static Dictionary<CostType, decimal> Budget(params (CostType Type, decimal Amount)[] lines) => lines.ToDictionary(l => l.Type, l => l.Amount);

    /// <summary>Tabela de casos do comprometido de material por saldo (CA 4): max(0, compra − baixa líquida) + previsto que não é compra.</summary>
    [Theory]
    [InlineData(10_000, 6_000, 0, 4_000)]
    [InlineData(10_000, 10_000, 0, 0)]
    [InlineData(1_000, 3_000, 0, 0)]
    [InlineData(1_000, 3_000, 500, 500)]
    [InlineData(0, 2_000, 0, 0)]
    public void Comprometido_de_material_por_saldo_nunca_negativo(decimal purchase, decimal issued, decimal planned, decimal expected)
    {
        var r = ProjectMarginCalculator.Compute(100_000m, Budget((CostType.Material, 5_000m)), [Pay(CostType.Material, planned: planned, purchase: purchase)],
            [Ledger(CostType.Material, ProjectCostSource.StockIssue, issued)]);
        LineOf(r, CostType.Material).Committed.Should().Be(expected);
        LineOf(r, CostType.Material).Realized.Should().Be(issued, "a compra só vira realizado na baixa");
    }

    [Fact]
    public void Realizado_soma_cada_fonte_da_natureza()
    {
        var r = ProjectMarginCalculator.Compute(100_000m, Budget(),
            [Pay(CostType.Material, confirmed: 750m), Pay(CostType.Labor, confirmed: 200m), Pay(CostType.Installation, confirmed: 3_000m), Pay(CostType.Freight, confirmed: 90m), Pay(CostType.Other, confirmed: 10m)],
            [
                Ledger(CostType.Material, ProjectCostSource.StockIssue, 1_000m), Ledger(CostType.Material, ProjectCostSource.StockReturn, -100m),
                Ledger(CostType.Labor, ProjectCostSource.Labor, 500m), Ledger(CostType.Labor, ProjectCostSource.LaborCorrection, -50m),
            ]);
        LineOf(r, CostType.Material).Realized.Should().Be(1_650m);
        LineOf(r, CostType.Labor).Realized.Should().Be(650m);
        LineOf(r, CostType.Installation).Realized.Should().Be(3_000m);
        LineOf(r, CostType.Freight).Realized.Should().Be(90m);
        LineOf(r, CostType.Other).Realized.Should().Be(10m);
    }

    [Fact]
    public void Previsto_fica_no_comprometido_e_nunca_no_realizado()
    {
        var r = ProjectMarginCalculator.Compute(100_000m, Budget((CostType.Installation, 8_000m)), [Pay(CostType.Installation, planned: 9_000m)], []);
        var line = LineOf(r, CostType.Installation);
        (line.Realized, line.Committed, line.Projected, line.Deviation, line.DeviationPct).Should().Be((0m, 9_000m, 9_000m, 1_000m, 12.5m));
    }

    [Fact]
    public void Guia_de_imposto_nunca_entra_no_custo_do_projeto()
    {
        var r = ProjectMarginCalculator.Compute(100_000m, Budget((CostType.Tax, 10_000m)), [Pay(CostType.Tax, confirmed: 2_000m, planned: 500m, guide: 300m), Pay(CostType.Other, guide: 100m)], []);
        var tax = LineOf(r, CostType.Tax);
        (tax.Realized, tax.Committed, tax.Projected).Should().Be((0m, 0m, 10_000m), "guia é da empresa, vai para o DRE (CA 3)");
        r.TaxGuides.Should().Be(2_900m);
        r.Total.Realized.Should().Be(0m);
    }

    [Theory]
    [InlineData(0, 0, 10_000, TaxProjectionBasis.Budgeted)]
    [InlineData(900, 30_000, 7_900, TaxProjectionBasis.Invoices)]
    [InlineData(3_200, 100_000, 3_200, TaxProjectionBasis.Invoices)]
    [InlineData(3_400, 120_000, 3_400, TaxProjectionBasis.Invoices)]
    public void Imposto_orcado_ate_a_nota_e_das_notas_mais_orcado_no_restante(decimal invoiceTax, decimal invoiced, decimal expectedProjected, TaxProjectionBasis basis)
    {
        LedgerCostTotals[] ledger = invoiceTax == 0 ? [] : [Ledger(CostType.Tax, ProjectCostSource.InvoiceTax, invoiceTax, invoiced)];
        var r = ProjectMarginCalculator.Compute(100_000m, Budget((CostType.Tax, 10_000m)), [], ledger);
        var tax = LineOf(r, CostType.Tax);
        tax.Realized.Should().Be(invoiceTax);
        tax.Projected.Should().Be(expectedProjected);
        tax.Projected.Should().BeGreaterThanOrEqualTo(tax.Realized);
        r.TaxBasis.Should().Be(basis);
    }

    [Fact]
    public void Estorno_de_imposto_volta_ao_orcado()
    {
        var r = ProjectMarginCalculator.Compute(100_000m, Budget((CostType.Tax, 10_000m)), [],
            [Ledger(CostType.Tax, ProjectCostSource.InvoiceTax, 900m, 30_000m), Ledger(CostType.Tax, ProjectCostSource.InvoiceTaxReversal, -900m, -30_000m)]);
        (LineOf(r, CostType.Tax).Realized, LineOf(r, CostType.Tax).Projected, r.TaxBasis).Should().Be((0m, 10_000m, TaxProjectionBasis.Budgeted));
    }

    [Fact]
    public void Projetado_nunca_abaixo_do_realizado_e_desvio_sem_orcado_e_traco()
    {
        var r = ProjectMarginCalculator.Compute(100_000m, Budget((CostType.Freight, 500m)), [Pay(CostType.Freight, confirmed: 800m), Pay(CostType.Other, confirmed: 50m)], []);
        LineOf(r, CostType.Freight).Projected.Should().Be(800m);
        LineOf(r, CostType.Other).DeviationPct.Should().BeNull("orçado 0 → \"—\"");
    }

    [Fact]
    public void Receita_zero_da_margem_em_traco()
    {
        var r = ProjectMarginCalculator.Compute(0m, Budget((CostType.Labor, 1_000m)), [], []);
        (r.BudgetedMarginPct, r.RealizedMarginPct, r.ProjectedMarginPct, r.MarginDeviationPp).Should().Be((null, null, null, null));
        r.ProjectedMargin.Should().Be(-1_000m);
    }

    [Fact]
    public void Percentuais_com_quatro_casas()
    {
        var r = ProjectMarginCalculator.Compute(30_000m, Budget((CostType.Labor, 10_000m)), [], []);
        r.BudgetedMarginPct.Should().Be(66.6667m);
        ProjectMarginCalculator.Pct(1, 3).Should().Be(33.3333m);
    }

    [Fact]
    public void Consolidado_e_ponderado_pela_receita_e_nao_media_simples()
    {
        var big = ProjectMarginCalculator.Compute(900_000m, Budget((CostType.Material, 810_000m)), [], []);
        var small = ProjectMarginCalculator.Compute(100_000m, Budget((CostType.Material, 50_000m)), [], []);
        var all = ProjectMarginCalculator.Consolidate([big, small]);
        (big.BudgetedMarginPct, small.BudgetedMarginPct).Should().Be((10m, 50m));
        all.BudgetedMarginPct.Should().Be(14m, "(1.000.000 − 860.000) ÷ 1.000.000; a média simples daria 30%");
        all.Revenue.Should().Be(1_000_000m);
    }

    [Fact]
    public void Dataset_fixo_dos_4_projetos_confere_com_a_planilha()
    {
        foreach (var p in MarginDataset.Projects)
        {
            var r = ProjectMarginCalculator.Compute(p.Revenue, p.Budget, p.Payables.Select(x => x.Totals).GroupBy(t => t.CostType)
                .Select(g => new PayableCostTotals(g.Key, g.Sum(t => t.Confirmed), g.Sum(t => t.Planned), g.Sum(t => t.StockPurchase), g.Sum(t => t.TaxGuide))), p.Ledger);
            MarginDataset.Check(p, ProjectCosts.Figures(r));
        }

        var consolidated = ProjectCosts.Figures(ProjectMarginCalculator.Consolidate(MarginDataset.Projects.Select(p => ProjectMarginCalculator.Compute(p.Revenue, p.Budget,
            p.Payables.Select(x => x.Totals), p.Ledger))));
        MarginDataset.CheckConsolidated(consolidated);
    }

    [Theory]
    [InlineData(DocumentOrigin.Purchase, CostType.Material, PayableStatus.Planned, PayableCostBucket.StockPurchase)]
    [InlineData(DocumentOrigin.Purchase, CostType.Material, PayableStatus.Paid, PayableCostBucket.StockPurchase)]
    [InlineData(DocumentOrigin.Purchase, CostType.Freight, PayableStatus.Planned, PayableCostBucket.Planned)]
    [InlineData(DocumentOrigin.Manual, CostType.Material, PayableStatus.Open, PayableCostBucket.Confirmed)]
    [InlineData(DocumentOrigin.Installer, CostType.Installation, PayableStatus.Planned, PayableCostBucket.Planned)]
    [InlineData(DocumentOrigin.Opening, CostType.Installation, PayableStatus.Planned, PayableCostBucket.Confirmed)]
    [InlineData(DocumentOrigin.Manual, CostType.Tax, PayableStatus.Open, PayableCostBucket.TaxGuide)]
    [InlineData(DocumentOrigin.Manual, null, PayableStatus.Open, PayableCostBucket.Confirmed)]
    public void Regra_unica_de_classificacao_do_titulo(DocumentOrigin origin, CostType? type, PayableStatus status, PayableCostBucket expected) =>
        PayableCostRules.Bucket(origin, type, status).Should().Be(expected);

    [Fact]
    public void Titulo_cancelado_nao_entra() => PayableCostRules.Bucket(DocumentOrigin.Manual, CostType.Freight, PayableStatus.Cancelled).Should().BeNull();
}

/// <summary>
/// Dataset fixo da DSH-02 CA 7, conferido centavo a centavo (a "planilha"):
/// <list type="number">
/// <item>P1 (receita 90.000 + aditivo 10.000 que soma 2.000 ao orçado de material): baixa 8.500,00, devolução 1.250,50, horas 1.200,00 com correção
/// −150,25 e 600,00.</item>
/// <item>P2 (60.000): instalação confirmada 7.500,00 e previsto do instalador 2.300,00 (acima do orçado de 8.000), frete pago 1.180,40.</item>
/// <item>P3 (50.000): compra de item estocável 6.000,00 previsto + 4.000,00 confirmado, baixa 6.000,00 e material entregue na obra 750,00.</item>
/// <item>P4 (40.000): compra de 1.000,00 menor que a baixa de 3.000,00 de estoque anterior (comprometido 0, sem crédito), guia de imposto
/// 1.500,00 com o projeto e uma linha <c>InvoiceTax</c> de 1.320,00 sobre nota de 12.000,00.</item>
/// </list>
/// </summary>
public static class MarginDataset
{
    public sealed record DatasetPayable(DocumentOrigin Origin, CostType CostType, PayableStatus Status, decimal Amount)
    {
        public PayableCostTotals Totals => PayableCostRules.Bucket(Origin, CostType, Status) switch
        {
            PayableCostBucket.Confirmed => new(CostType, Amount, 0, 0, 0),
            PayableCostBucket.Planned => new(CostType, 0, Amount, 0, 0),
            PayableCostBucket.StockPurchase => new(CostType, 0, 0, Amount, 0),
            _ => new(CostType, 0, 0, 0, Amount),
        };
    }

    public sealed record Expected(CostType CostType, decimal Budgeted, decimal Realized, decimal Committed, decimal Projected, decimal Deviation);

    public sealed record DatasetProject(
        string Name, decimal ContractValue, decimal AmendmentValue, decimal AmendmentMaterial, Dictionary<CostType, decimal> Budget, DatasetPayable[] Payables, LedgerCostTotals[] Ledger,
        Expected[] Lines, Expected Total, decimal? BudgetedPct, decimal RealizedMargin, decimal? RealizedPct, decimal? ProjectedPct, decimal? Pp, ProjectTaxBasis TaxBasis, decimal TaxGuides)
    {
        public decimal Revenue => ContractValue + AmendmentValue;
    }

    private static LedgerCostTotals L(CostType type, ProjectCostSource source, decimal amount, decimal invoice = 0) => new(type, source, false, amount, invoice);

    public static readonly DatasetProject[] Projects =
    [
        new("P1 material e horas", 90_000m, 10_000m, 2_000m, new() { [CostType.Material] = 30_000m, [CostType.Labor] = 5_000m, [CostType.Tax] = 12_000m }, [],
            [
                L(CostType.Material, ProjectCostSource.StockIssue, 8_500m), L(CostType.Material, ProjectCostSource.StockReturn, -1_250.50m),
                L(CostType.Labor, ProjectCostSource.Labor, 1_800m), L(CostType.Labor, ProjectCostSource.LaborCorrection, -150.25m),
            ],
            [new(CostType.Material, 30_000m, 7_249.50m, 0m, 30_000m, 0m), new(CostType.Labor, 5_000m, 1_649.75m, 0m, 5_000m, 0m), new(CostType.Tax, 12_000m, 0m, 0m, 12_000m, 0m)],
            new(CostType.Other, 47_000m, 8_899.25m, 0m, 47_000m, 0m), 53m, 91_100.75m, 91.1008m, 53m, 0m, ProjectTaxBasis.Budgeted, 0m),
        new("P2 instalação acima", 60_000m, 0m, 0m, new() { [CostType.Installation] = 8_000m, [CostType.Freight] = 1_000m, [CostType.Tax] = 6_000m },
            [
                new(DocumentOrigin.Manual, CostType.Installation, PayableStatus.Open, 7_500m), new(DocumentOrigin.Installer, CostType.Installation, PayableStatus.Planned, 2_300m),
                new(DocumentOrigin.Manual, CostType.Freight, PayableStatus.Paid, 1_180.40m),
            ], [],
            [new(CostType.Installation, 8_000m, 7_500m, 2_300m, 9_800m, 1_800m), new(CostType.Freight, 1_000m, 1_180.40m, 0m, 1_180.40m, 180.40m), new(CostType.Tax, 6_000m, 0m, 0m, 6_000m, 0m)],
            new(CostType.Other, 15_000m, 8_680.40m, 2_300m, 16_980.40m, 1_980.40m), 75m, 51_319.60m, 85.5327m, 71.6993m, -3.3007m, ProjectTaxBasis.Budgeted, 0m),
        new("P3 compra maior que a baixa", 50_000m, 0m, 0m, new() { [CostType.Material] = 9_000m, [CostType.Labor] = 3_000m, [CostType.Tax] = 5_000m },
            [
                new(DocumentOrigin.Purchase, CostType.Material, PayableStatus.Planned, 6_000m), new(DocumentOrigin.Purchase, CostType.Material, PayableStatus.Open, 4_000m),
                new(DocumentOrigin.Manual, CostType.Material, PayableStatus.Open, 750m),
            ],
            [L(CostType.Material, ProjectCostSource.StockIssue, 6_000m)],
            [new(CostType.Material, 9_000m, 6_750m, 4_000m, 10_750m, 1_750m), new(CostType.Labor, 3_000m, 0m, 0m, 3_000m, 0m), new(CostType.Tax, 5_000m, 0m, 0m, 5_000m, 0m)],
            new(CostType.Other, 17_000m, 6_750m, 4_000m, 18_750m, 1_750m), 66m, 43_250m, 86.5m, 62.5m, -3.5m, ProjectTaxBasis.Budgeted, 0m),
        new("P4 guia e nota", 40_000m, 0m, 0m, new() { [CostType.Material] = 2_000m, [CostType.Tax] = 4_800m },
            [new(DocumentOrigin.Purchase, CostType.Material, PayableStatus.Open, 1_000m), new(DocumentOrigin.Manual, CostType.Tax, PayableStatus.Open, 1_500m)],
            [L(CostType.Material, ProjectCostSource.StockIssue, 3_000m), L(CostType.Tax, ProjectCostSource.InvoiceTax, 1_320m, 12_000m)],
            [new(CostType.Material, 2_000m, 3_000m, 0m, 3_000m, 1_000m), new(CostType.Tax, 4_800m, 1_320m, 0m, 4_680m, -120m)],
            new(CostType.Other, 6_800m, 4_320m, 0m, 7_680m, 880m), 83m, 35_680m, 89.2m, 80.8m, -2.2m, ProjectTaxBasis.Invoices, 1_500m),
    ];

    /// <summary>Ordem do ranking (CA 9): pior desvio da margem primeiro.</summary>
    public static readonly string[] RankingOrder = ["P3 compra maior que a baixa", "P2 instalação acima", "P4 guia e nota", "P1 material e horas"];

    public static void Check(DatasetProject p, ProjectMarginFigures f)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(f);
        foreach (var e in p.Lines)
        {
            var l = f.Lines.Single(x => x.CostType == e.CostType);
            (l.Budgeted, l.Realized, l.Committed, l.Projected, l.Deviation).Should().Be((e.Budgeted, e.Realized, e.Committed, e.Projected, e.Deviation), $"{p.Name}: {e.CostType}");
        }

        (f.Total.Budgeted, f.Total.Realized, f.Total.Committed, f.Total.Projected, f.Total.Deviation)
            .Should().Be((p.Total.Budgeted, p.Total.Realized, p.Total.Committed, p.Total.Projected, p.Total.Deviation), $"{p.Name}: total");
        (f.Revenue, f.BudgetedMarginPct, f.RealizedMargin, f.RealizedMarginPct, f.ProjectedMarginPct, f.MarginDeviationPp, f.TaxBasis, f.TaxGuides)
            .Should().Be((p.Revenue, p.BudgetedPct, p.RealizedMargin, p.RealizedPct, p.ProjectedPct, p.Pp, p.TaxBasis, p.TaxGuides), $"{p.Name}: margens");
    }

    public static void CheckConsolidated(ProjectMarginFigures f)
    {
        ArgumentNullException.ThrowIfNull(f);
        (f.Revenue, f.Total.Budgeted, f.Total.Realized, f.Total.Committed, f.Total.Projected).Should().Be((250_000m, 85_800m, 28_649.65m, 6_300m, 90_410.40m));
        (f.BudgetedMarginPct, f.RealizedMarginPct, f.ProjectedMarginPct, f.MarginDeviationPp).Should().Be((65.68m, 88.5401m, 63.8358m, -1.8442m), "ponderado pela receita (Σ ÷ Σ)");
        var material = f.Lines.Single(l => l.CostType == CostType.Material);
        (material.Budgeted, material.Realized, material.Committed, material.Projected, material.Deviation).Should().Be((41_000m, 16_999.50m, 4_000m, 43_750m, 2_750m));
        f.Lines.Single(l => l.CostType == CostType.Tax).Projected.Should().Be(27_680m);
        f.TaxGuides.Should().Be(1_500m);
        ProjectCosts.Worst(f).Should().Be(CostType.Material);
    }
}
