using BemAqui.Commercial.Application;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>COM-05 CA 3, 8 e 9: projetado com restante nunca negativo, desvio e margem projetada.</summary>
public class ProjectCostsTests
{
    private static readonly Dictionary<CostType, decimal> Budget = new() { [CostType.Installation] = 10_000m, [CostType.Material] = 30_000m };

    [Fact]
    public void Exemplo_da_story_instalacao_dentro_e_depois_acima_do_orcado()
    {
        var first = ProjectCosts.Compute(60_000m, Budget, [(CostType.Installation, 8_000m, false)]);
        var installation = first.Lines.Single(l => l.CostType == CostType.Installation);
        installation.Realized.Should().Be(8_000m);
        installation.Projected.Should().Be(10_000m);
        installation.Deviation.Should().Be(0m);
        installation.DeviationPct.Should().Be(0m);

        var second = ProjectCosts.Compute(60_000m, Budget, [(CostType.Installation, 8_000m, false), (CostType.Installation, 3_000m, false), (CostType.Installation, 999m, true)]);
        installation = second.Lines.Single(l => l.CostType == CostType.Installation);
        installation.Realized.Should().Be(11_000m);
        installation.Projected.Should().Be(11_000m);
        installation.Deviation.Should().Be(1_000m);
        installation.DeviationPct.Should().Be(10m);
        second.Total.Projected.Should().Be(41_000m);
        second.BudgetedMarginPct.Should().Be(33.3m);
        second.ProjectedMarginPct.Should().Be(31.7m);
    }

    [Fact]
    public void Natureza_sem_orcado_e_titulo_sem_natureza_vao_para_outros()
    {
        var view = ProjectCosts.Compute(0m, Budget, [(null, 500m, false)]);
        view.Lines.Single(l => l.CostType == CostType.Other).Realized.Should().Be(500m);
        view.Lines.Single(l => l.CostType == CostType.Other).DeviationPct.Should().BeNull();
        view.ProjectedMarginPct.Should().BeNull();
    }
}
