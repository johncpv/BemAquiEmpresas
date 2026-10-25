using BemAqui.Commercial.Application;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// Regressão da COM-05 (PRD-04 CA 11, ADR 0025 §4, D17 (a)) com dados sintéticos que imitam os projetos da Fase 2 do tenant
/// <c>konebras</c>: título previsto do instalador (gerado com a OS de instalação), títulos confirmados (frete, instalador, outros) e
/// título de implantação. O cálculo antigo somava todo título não cancelado como realizado; o novo tira os previstos do realizado e os
/// mostra no comprometido. Esperado em cada projeto: o realizado cai exatamente pelos títulos <c>Planned</c>, esse valor aparece no
/// comprometido e o projetado total não muda (sem horas na razão, que só nascem com a PRD-04).
/// </summary>
public class ProjectCostsRegressionTests
{
    /// <summary>Cálculo da COM-05 antes da PRD-04: realizado = todo título não cancelado; projetado = realizado + max(orçado − realizado, 0).</summary>
    private static (decimal Realized, decimal Projected) Legacy(IReadOnlyDictionary<CostType, decimal> budget, IReadOnlyList<CostPayable> payables)
    {
        var realized = payables.Where(p => p.Status != PayableStatus.Cancelled).GroupBy(p => p.CostType ?? CostType.Other).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var projected = Enum.GetValues<CostType>().Sum(t => realized.GetValueOrDefault(t) + Math.Max(budget.GetValueOrDefault(t) - realized.GetValueOrDefault(t), 0));
        return (realized.Values.Sum(), projected);
    }

    public static TheoryData<string, decimal, Dictionary<CostType, decimal>, CostPayable[]> Projects => new()
    {
        {
            // Elevador com OS de instalação aberta: previsto do instalador dentro do orçado, frete confirmado.
            "Projeto com previsto do instalador", 180_000m,
            new() { [CostType.Material] = 60_000m, [CostType.Labor] = 15_000m, [CostType.Installation] = 18_000m, [CostType.Freight] = 2_500m, [CostType.Tax] = 21_000m },
            [new(CostType.Installation, DocumentOrigin.Installer, PayableStatus.Planned, 16_500m), new(CostType.Freight, DocumentOrigin.Manual, PayableStatus.Open, 2_200m)]
        },
        {
            // Previsto acima do orçado (o comprometido manda no projetado) e um cancelado.
            "Projeto com previsto acima do orçado", 95_000m,
            new() { [CostType.Installation] = 9_000m, [CostType.Freight] = 1_000m },
            [
                new(CostType.Installation, DocumentOrigin.Installer, PayableStatus.Planned, 11_000m), new(CostType.Freight, DocumentOrigin.Manual, PayableStatus.Paid, 1_400m),
                new(CostType.Other, DocumentOrigin.Manual, PayableStatus.Cancelled, 700m),
            ]
        },
        {
            // Instalação já confirmada e paga, sem previsto: nada muda.
            "Projeto sem previsto", 120_000m,
            new() { [CostType.Installation] = 12_000m, [CostType.Freight] = 1_500m },
            [new(CostType.Installation, DocumentOrigin.Installer, PayableStatus.Paid, 12_000m), new(CostType.Freight, DocumentOrigin.Manual, PayableStatus.Open, 1_800m)]
        },
        {
            // Projeto migrado com título de implantação (saldo da data de corte) e um previsto sem natureza.
            "Projeto da implantação", 60_000m,
            new() { [CostType.Installation] = 6_000m },
            [new(CostType.Installation, DocumentOrigin.Opening, PayableStatus.Open, 4_000m), new(null, DocumentOrigin.Manual, PayableStatus.Planned, 300m)]
        },
    };

    [Theory]
    [MemberData(nameof(Projects))]
    public void Realizado_cai_pelos_previstos_que_vao_para_o_comprometido_e_o_projetado_nao_muda(string name, decimal revenue, Dictionary<CostType, decimal> budget, CostPayable[] payables)
    {
        var before = Legacy(budget, payables);
        var after = ProjectCosts.Compute(revenue, budget, payables, []);
        var planned = payables.Where(p => p.Status == PayableStatus.Planned).Sum(p => p.Amount);

        after.Total.Realized.Should().Be(before.Realized - planned, $"{name}: o realizado cai exatamente pelos títulos previstos");
        after.Total.Committed.Should().Be(planned, $"{name}: os previstos aparecem no comprometido");
        after.Total.Projected.Should().Be(before.Projected, $"{name}: o projetado total não muda");
        if (planned == 0)
        {
            after.Total.Realized.Should().Be(before.Realized, $"{name}: sem previsto, nada muda");
        }
    }

    /// <summary>O "antes → depois" documentado na story (PRD-04, Change Log) para o primeiro projeto.</summary>
    [Fact]
    public void Antes_e_depois_do_projeto_com_previsto_do_instalador()
    {
        var budget = new Dictionary<CostType, decimal> { [CostType.Material] = 60_000m, [CostType.Labor] = 15_000m, [CostType.Installation] = 18_000m, [CostType.Freight] = 2_500m, [CostType.Tax] = 21_000m };
        CostPayable[] payables = [new(CostType.Installation, DocumentOrigin.Installer, PayableStatus.Planned, 16_500m), new(CostType.Freight, DocumentOrigin.Manual, PayableStatus.Open, 2_200m)];
        var before = Legacy(budget, payables);
        var after = ProjectCosts.Compute(180_000m, budget, payables, []);

        (before.Realized, before.Projected).Should().Be((18_700m, 116_500m));
        (after.Total.Realized, after.Total.Committed, after.Total.Projected).Should().Be((2_200m, 16_500m, 116_500m));
        var installation = after.Lines.Single(l => l.CostType == CostType.Installation);
        (installation.Realized, installation.Committed, installation.Projected).Should().Be((0m, 16_500m, 18_000m));
        after.ProjectedMarginPct.Should().Be(35.2778m);
    }
}