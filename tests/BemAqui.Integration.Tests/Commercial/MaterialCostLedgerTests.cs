using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// EST-03 CA 8 e 9 (ADR 0025 §1 e §2), sem banco: a baixa soma e a devolução subtrai na razão; estorno chegando antes do lançamento
/// grava a diferença certa; repetido não grava; produção e expedição nunca entram; dataset fixo do realizado de material.
/// </summary>
public class MaterialCostLedgerTests
{
    /// <summary>Aplica os fatos (tipo, seq, valor novo) na ordem dada, como o consumidor faz com uma fonte só.</summary>
    private static List<AppliedCost> Apply(StockMovementType type, params (int Seq, decimal NewAmount)[] events)
    {
        var applied = new List<AppliedCost>();
        foreach (var (seq, amount) in events)
        {
            if (ProjectCostLedger.Difference(applied, seq, MaterialCostLedger.SignedAmount(type, amount)) is { } diff)
            {
                applied.Add(new AppliedCost(seq, diff));
            }
        }

        return applied;
    }

    [Fact]
    public void Baixa_soma_e_o_estorno_zera()
    {
        Apply(StockMovementType.Issue, (1, 1200m), (2, 0m)).Select(a => a.Amount).Should().Equal(1200m, -1200m);
    }

    [Fact]
    public void Estorno_antes_do_lancamento_e_repetidos_deixam_a_fonte_em_zero()
    {
        var rows = Apply(StockMovementType.Issue, (2, 0m), (1, 1200m), (2, 0m), (1, 1200m));
        rows.Should().ContainSingle("o lançamento que chega depois do estorno é ignorado");
        rows.Sum(r => r.Amount).Should().Be(0m);
    }

    [Fact]
    public void Devolucao_entra_negativa_e_o_estorno_dela_volta_a_zero()
    {
        Apply(StockMovementType.Return, (1, 200m)).Single().Amount.Should().Be(-200m);
        Apply(StockMovementType.Return, (1, 200m), (2, 0m)).Sum(r => r.Amount).Should().Be(0m);
    }

    [Theory]
    [InlineData(StockMovementType.Production)]
    [InlineData(StockMovementType.Shipment)]
    [InlineData(StockMovementType.Receipt)]
    [InlineData(StockMovementType.Transfer)]
    public void Producao_expedicao_e_os_demais_nunca_entram_no_custo_do_projeto(StockMovementType type) =>
        MaterialCostLedger.SourceOf(type).Should().BeNull();

    [Fact]
    public void Dataset_fixo_baixa_de_1200_e_devolucao_de_200_da_realizado_de_material_1000()
    {
        var ledger = Apply(StockMovementType.Issue, (1, 1200m)).Select(a => new CostLedgerLine(CostType.Material, ProjectCostSource.StockIssue, a.Amount, false))
            .Concat(Apply(StockMovementType.Return, (1, 200m)).Select(a => new CostLedgerLine(CostType.Material, ProjectCostSource.StockReturn, a.Amount, false)))
            .ToList();

        var view = ProjectCosts.Compute(10_000m, new Dictionary<CostType, decimal> { [CostType.Material] = 3_000m }, Array.Empty<CostPayable>(), ledger);

        var material = view.Lines.Single(l => l.CostType == CostType.Material);
        material.Realized.Should().Be(1_000m);
        material.Projected.Should().Be(3_000m, "realizado 1.000 + max(orçado − realizado 2.000, comprometido 0)");
        view.ProjectedMarginPct.Should().Be(70m);
    }

    [Fact]
    public void Linha_do_tempo_mostra_baixa_e_devolucao_com_valor()
    {
        var order = Guid.NewGuid();
        ProjectService.MaterialEntry(order, "OS-2026-00012", ProjectCostSource.StockIssue, 1200m, DateTimeOffset.UtcNow, false).Title
            .Should().Be("Material baixado para a OS-2026-00012: R$ 1.200,00");
        var back = ProjectService.MaterialEntry(order, "OS-2026-00012", ProjectCostSource.StockReturn, -200m, DateTimeOffset.UtcNow, true);
        back.Title.Should().Be("Material devolvido da OS-2026-00012: R$ 200,00 (calculado na implantação)");
        back.Link.Should().Be("/app/producao/os/" + order + "?aba=material");
    }
}
