using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>PRD-04 CA 8 (ADR 0025 §2): razão de diferenças com eventos repetidos e fora de ordem, sem banco.</summary>
public class ProjectCostLedgerTests
{
    /// <summary>Aplica os eventos (versão, valor novo) na ordem dada, como o consumidor: grava a diferença ou ignora.</summary>
    private static List<AppliedCost> Apply(params (int Seq, decimal NewAmount)[] events)
    {
        var applied = new List<AppliedCost>();
        foreach (var (seq, amount) in events)
        {
            if (ProjectCostLedger.Difference(applied, seq, amount) is { } diff)
            {
                applied.Add(new AppliedCost(seq, diff));
            }
        }

        return applied;
    }

    [Fact]
    public void Fechamento_correcao_e_recalculo_em_ordem_gravam_as_diferencas()
    {
        var rows = Apply((1, 120m), (2, 144m), (3, 150m));
        rows.Select(r => r.Amount).Should().Equal(120m, 24m, 6m);
        rows.Sum(r => r.Amount).Should().Be(150m);
    }

    [Fact]
    public void Fora_de_ordem_e_repetidos_deixam_a_soma_no_valor_final()
    {
        Apply((3, 150m), (1, 120m), (2, 144m), (3, 150m), (1, 120m)).Sum(r => r.Amount).Should().Be(150m);
        Apply((2, 144m), (1, 120m), (2, 144m), (3, 150m)).Sum(r => r.Amount).Should().Be(150m);
        Apply((1, 120m), (1, 120m)).Should().ContainSingle("o mesmo seq não grava de novo");
    }

    [Fact]
    public void Correcao_para_menos_grava_diferenca_negativa_e_mensagem_antiga_e_ignorada()
    {
        Apply((1, 120m), (2, 96m)).Select(r => r.Amount).Should().Equal(120m, -24m);
        ProjectCostLedger.Difference([], 0, 100m).Should().BeNull("mensagem de antes da PRD-04 (versão 0)");
    }

    [Fact]
    public void Primeira_versao_e_Labor_e_as_seguintes_LaborCorrection()
    {
        ProjectCostLedger.LaborSource(1).Should().Be(ProjectCostSource.Labor);
        ProjectCostLedger.LaborSource(2).Should().Be(ProjectCostSource.LaborCorrection);
        ProjectCostLedger.LaborSource(7).Should().Be(ProjectCostSource.LaborCorrection);
    }
}