using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
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

    /// <summary>
    /// Cálculo da PRD-04 (antes da DSH-02), copiado do <c>ProjectCosts.Compute</c> daquela versão: título de natureza Impostos entrava como
    /// qualquer outro (guia no realizado) e o imposto projetado seguia a regra geral.
    /// </summary>
    private static decimal Prd04Projected(IReadOnlyDictionary<CostType, decimal> budget, IReadOnlyList<CostPayable> payables, IReadOnlyList<CostLedgerLine> ledger)
    {
        var realized = new Dictionary<CostType, decimal>();
        var committed = new Dictionary<CostType, decimal>();
        var purchased = new Dictionary<CostType, decimal>();
        var issued = new Dictionary<CostType, decimal>();
        foreach (var p in payables.Where(p => p.Status != PayableStatus.Cancelled))
        {
            var type = p.CostType ?? CostType.Other;
            var target = p is { Origin: DocumentOrigin.Purchase, CostType: CostType.Material } ? purchased : p.Origin == DocumentOrigin.Opening || p.Status != PayableStatus.Planned ? realized : committed;
            target[type] = target.GetValueOrDefault(type) + p.Amount;
        }

        foreach (var l in ledger)
        {
            realized[l.CostType] = realized.GetValueOrDefault(l.CostType) + l.Amount;
            if (l.Source is ProjectCostSource.StockIssue or ProjectCostSource.StockReturn)
            {
                issued[l.CostType] = issued.GetValueOrDefault(l.CostType) + l.Amount;
            }
        }

        foreach (var (type, amount) in purchased)
        {
            committed[type] = committed.GetValueOrDefault(type) + Math.Max(0, amount - issued.GetValueOrDefault(type));
        }

        return Enum.GetValues<CostType>().Sum(t => realized.GetValueOrDefault(t) + Math.Max(budget.GetValueOrDefault(t) - realized.GetValueOrDefault(t), committed.GetValueOrDefault(t)));
    }

    public static TheoryData<string, Dictionary<CostType, decimal>, CostPayable[], CostLedgerLine[], decimal> Dsh02Projects => new()
    {
        {
            "Sem compra e sem guia", new() { [CostType.Material] = 20_000m, [CostType.Labor] = 4_000m, [CostType.Installation] = 6_000m, [CostType.Tax] = 9_000m },
            [new(CostType.Installation, DocumentOrigin.Installer, PayableStatus.Planned, 6_500m)],
            [new(CostType.Material, ProjectCostSource.StockIssue, 12_000m, false), new(CostType.Labor, ProjectCostSource.Labor, 4_400m, false)], 0m
        },
        {
            "Com compra de item estocável", new() { [CostType.Material] = 10_000m, [CostType.Tax] = 3_000m },
            [new(CostType.Material, DocumentOrigin.Purchase, PayableStatus.Planned, 14_000m)], [new(CostType.Material, ProjectCostSource.StockIssue, 5_000m, false)], 0m
        },
        {
            // Causa da diferença: guia fora do projeto. A PRD-04 punha a guia de 4.000 no realizado de Impostos (acima do orçado de 3.000).
            "Com guia de imposto", new() { [CostType.Installation] = 5_000m, [CostType.Tax] = 3_000m },
            [new(CostType.Tax, DocumentOrigin.Manual, PayableStatus.Paid, 4_000m), new(CostType.Installation, DocumentOrigin.Manual, PayableStatus.Open, 5_000m)], [], -1_000m
        },
    };

    /// <summary>
    /// DSH-02 CA 8 (adendo D8 da ADR 0025): o projetado total de projeto sem guia de imposto com projeto é igual ao da PRD-04 (o comprometido por
    /// saldo já era da PRD-04); a diferença do projeto com guia é listada com a causa. A regressão sobre a cópia do <c>konebras</c> roda na
    /// homologação com o mesmo par de cálculos (Change Log da story).
    /// </summary>
    [Theory]
    [MemberData(nameof(Dsh02Projects))]
    public void Projetado_total_igual_ao_da_PRD_04_exceto_pela_guia(string name, Dictionary<CostType, decimal> budget, CostPayable[] payables, CostLedgerLine[] ledger, decimal expectedDifference)
    {
        var before = Prd04Projected(budget, payables, ledger);
        var after = ProjectCosts.Compute(100_000m, budget, payables, ledger);
        (after.Total.Projected - before).Should().Be(expectedDifference, $"{name}: {(expectedDifference == 0 ? "sem diferença" : "guia de imposto fora do projeto")}");
    }
}