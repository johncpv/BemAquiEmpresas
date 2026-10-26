using BemAqui.Commercial.Application;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// COM-05 CA 3, 8 e 9 e PRD-04 CA 9 (ADR 0025 §1 e adendo): realizado só com título confirmado e a razão, comprometido com o previsto
/// e a compra de item estocável por saldo, projetado = realizado + max(orçado − realizado, comprometido), desvio e margem projetada.
/// </summary>
public class ProjectCostsTests
{
    private static readonly Dictionary<CostType, decimal> Budget = new() { [CostType.Installation] = 10_000m, [CostType.Material] = 30_000m };

    private static CostPayable Pay(CostType? type, decimal amount, PayableStatus status = PayableStatus.Open, DocumentOrigin origin = DocumentOrigin.Manual) => new(type, origin, status, amount);

    private static CostLedgerLine Ledger(CostType type, decimal amount, ProjectCostSource source = ProjectCostSource.Labor, bool backfill = false) => new(type, source, amount, backfill);

    private static ProjectMarginLine LineOf(ProjectMarginFigures view, CostType type) => view.Lines.Single(l => l.CostType == type);

    [Fact]
    public void Exemplo_da_story_instalacao_dentro_e_depois_acima_do_orcado()
    {
        var first = ProjectCosts.Compute(60_000m, Budget, [Pay(CostType.Installation, 8_000m)], []);
        var installation = LineOf(first, CostType.Installation);
        installation.Realized.Should().Be(8_000m);
        installation.Committed.Should().Be(0m);
        installation.Projected.Should().Be(10_000m);
        installation.Deviation.Should().Be(0m);
        installation.DeviationPct.Should().Be(0m);

        var second = ProjectCosts.Compute(60_000m, Budget,
            [Pay(CostType.Installation, 8_000m), Pay(CostType.Installation, 3_000m, PayableStatus.Paid), Pay(CostType.Installation, 999m, PayableStatus.Cancelled)], []);
        installation = LineOf(second, CostType.Installation);
        installation.Realized.Should().Be(11_000m);
        installation.Projected.Should().Be(11_000m);
        installation.Deviation.Should().Be(1_000m);
        installation.DeviationPct.Should().Be(10m);
        second.Total.Projected.Should().Be(41_000m);
        second.BudgetedMarginPct.Should().Be(33.3333m);
        second.ProjectedMarginPct.Should().Be(31.6667m);
    }

    [Fact]
    public void Natureza_sem_orcado_e_titulo_sem_natureza_vao_para_outros()
    {
        var view = ProjectCosts.Compute(0m, Budget, [Pay(null, 500m)], []);
        LineOf(view, CostType.Other).Realized.Should().Be(500m);
        LineOf(view, CostType.Other).DeviationPct.Should().BeNull();
        view.ProjectedMarginPct.Should().BeNull();
    }

    [Fact]
    public void Titulo_previsto_vai_para_o_comprometido_e_o_confirmado_para_o_realizado()
    {
        var planned = ProjectCosts.Compute(60_000m, Budget, [Pay(CostType.Installation, 7_000m, PayableStatus.Planned, DocumentOrigin.Installer)], []);
        var line = LineOf(planned, CostType.Installation);
        line.Realized.Should().Be(0m, "o previsto do instalador não é gasto ainda (D2)");
        line.Committed.Should().Be(7_000m);
        line.Projected.Should().Be(10_000m, "realizado 0 + max(orçado 10.000, comprometido 7.000)");

        var over = ProjectCosts.Compute(60_000m, Budget, [Pay(CostType.Installation, 12_000m, PayableStatus.Planned, DocumentOrigin.Installer)], []);
        LineOf(over, CostType.Installation).Projected.Should().Be(12_000m, "o comprometido acima do orçado já aparece no projetado");
        LineOf(over, CostType.Installation).Deviation.Should().Be(2_000m);

        var confirmed = ProjectCosts.Compute(60_000m, Budget, [Pay(CostType.Installation, 7_000m, PayableStatus.Open, DocumentOrigin.Installer)], []);
        LineOf(confirmed, CostType.Installation).Realized.Should().Be(7_000m);
        LineOf(confirmed, CostType.Installation).Committed.Should().Be(0m);
    }

    [Theory]
    [InlineData(PayableStatus.AwaitingApproval)]
    [InlineData(PayableStatus.Approved)]
    [InlineData(PayableStatus.PartiallyPaid)]
    [InlineData(PayableStatus.Paid)]
    public void Toda_situacao_fora_de_previsto_e_cancelado_e_realizado(PayableStatus status) =>
        LineOf(ProjectCosts.Compute(1m, Budget, [Pay(CostType.Freight, 300m, status)], []), CostType.Freight).Realized.Should().Be(300m);

    [Fact]
    public void Titulo_de_implantacao_entra_no_realizado_pelo_saldo_da_data_de_corte()
    {
        var view = ProjectCosts.Compute(1m, Budget, [Pay(CostType.Installation, 5_000m, PayableStatus.Open, DocumentOrigin.Opening)], []);
        LineOf(view, CostType.Installation).Realized.Should().Be(5_000m);
    }

    [Fact]
    public void Compra_de_item_estocavel_e_comprometida_ate_a_baixa_por_saldo()
    {
        Pay(CostType.Material, 1m, PayableStatus.Open, DocumentOrigin.Purchase).Should().Match<CostPayable>(p => ProjectCosts.IsStockPurchase(p));
        var bought = ProjectCosts.Compute(60_000m, Budget,
            [Pay(CostType.Material, 10_000m, PayableStatus.Open, DocumentOrigin.Purchase), Pay(CostType.Material, 2_000m, PayableStatus.Planned, DocumentOrigin.Purchase)], []);
        LineOf(bought, CostType.Material).Realized.Should().Be(0m, "material comprado só vira realizado na baixa para a OS");
        LineOf(bought, CostType.Material).Committed.Should().Be(12_000m, "confirmado ou não");

        var issued = ProjectCosts.Compute(60_000m, Budget,
            [Pay(CostType.Material, 10_000m, PayableStatus.Open, DocumentOrigin.Purchase)],
            [Ledger(CostType.Material, 7_000m, ProjectCostSource.StockIssue), Ledger(CostType.Material, -1_000m, ProjectCostSource.StockReturn)]);
        LineOf(issued, CostType.Material).Realized.Should().Be(6_000m);
        LineOf(issued, CostType.Material).Committed.Should().Be(4_000m, "compra 10.000 − baixa líquida 6.000");

        var stock = ProjectCosts.Compute(60_000m, Budget, [Pay(CostType.Material, 1_000m, PayableStatus.Open, DocumentOrigin.Purchase)], [Ledger(CostType.Material, 3_000m, ProjectCostSource.StockIssue)]);
        LineOf(stock, CostType.Material).Committed.Should().Be(0m, "max(0, …): baixa de estoque anterior não vira crédito");
    }

    [Fact]
    public void Horas_da_razao_entram_no_realizado_com_a_marca_de_implantacao()
    {
        var view = ProjectCosts.Compute(60_000m, new Dictionary<CostType, decimal> { [CostType.Labor] = 1_000m },
            [], [Ledger(CostType.Labor, 120m), Ledger(CostType.Labor, 24m), Ledger(CostType.Labor, 50m, backfill: true)]);
        var labor = LineOf(view, CostType.Labor);
        labor.Realized.Should().Be(194m);
        labor.IncludesBackfill.Should().BeTrue("Calculado na implantação (CA 13)");
        view.Total.IncludesBackfill.Should().BeTrue();
        LineOf(view, CostType.Material).IncludesBackfill.Should().BeFalse();
    }

    /// <summary>
    /// CA 9, dataset fixo conferido com a planilha: 3 apontamentos de 2 cargos (R$ 48,00/h: 150 min = 120,00 e 90 min = 72,00;
    /// R$ 35,50/h: 200 min = 118,33), 1 título previsto do instalador (7.000,00) e 1 confirmado de frete (650,00).
    /// </summary>
    [Fact]
    public void Dataset_fixo_da_story_confere_com_a_planilha()
    {
        var budget = new Dictionary<CostType, decimal> { [CostType.Labor] = 1_000m, [CostType.Installation] = 8_000m, [CostType.Freight] = 500m };
        var view = ProjectCosts.Compute(15_000m, budget,
            [Pay(CostType.Installation, 7_000m, PayableStatus.Planned, DocumentOrigin.Installer), Pay(CostType.Freight, 650m, PayableStatus.Open)],
            [Ledger(CostType.Labor, 120m), Ledger(CostType.Labor, 72m), Ledger(CostType.Labor, 118.33m)]);

        var labor = LineOf(view, CostType.Labor);
        (labor.Realized, labor.Committed, labor.Projected, labor.Deviation).Should().Be((310.33m, 0m, 1_000m, 0m));
        var installation = LineOf(view, CostType.Installation);
        (installation.Realized, installation.Committed, installation.Projected, installation.Deviation).Should().Be((0m, 7_000m, 8_000m, 0m));
        var freight = LineOf(view, CostType.Freight);
        (freight.Realized, freight.Committed, freight.Projected, freight.Deviation, freight.DeviationPct).Should().Be((650m, 0m, 650m, 150m, 30m));
        (view.Total.Budgeted, view.Total.Realized, view.Total.Committed, view.Total.Projected, view.Total.Deviation).Should().Be((9_500m, 960.33m, 7_000m, 9_650m, 150m));
        view.BudgetedMarginPct.Should().Be(36.6667m);
        view.ProjectedMarginPct.Should().Be(35.6667m);
    }
}