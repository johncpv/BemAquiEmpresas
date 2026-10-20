using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>COM-04a CA 8: orçado do projeto por natureza, com os centavos fechando no custo total pelo maior resto.</summary>
public class ProjectBudgetTests
{
    [Fact]
    public void Centavos_fecham_no_custo_total_pelo_maior_resto()
    {
        var item = new QuoteItem { Quantity = 1.5m, UnitCost = 0.03m };
        var costs = new[] { CostType.Material, CostType.Labor, CostType.Freight }.Select(t => new QuoteItemCost { QuoteItemId = item.Id, CostType = t, Quantity = 1, UnitCost = 0.01m, Total = 0.01m }).ToList();
        var totalCost = QuotePricing.Money(item.Quantity * item.UnitCost);

        var budget = QuoteApprovalService.Budget(totalCost, [item], costs);

        totalCost.Should().Be(0.05m);
        budget.Should().Equal((CostType.Material, 0.02m), (CostType.Labor, 0.02m), (CostType.Freight, 0.01m));
        budget.Sum(b => b.Amount).Should().Be(totalCost);
    }

    [Fact]
    public void Custo_sem_composicao_vai_para_outros()
    {
        var composed = new QuoteItem { Quantity = 3m, UnitCost = 60.01m };
        var bare = new QuoteItem { Quantity = 2m, UnitCost = 10m };
        var costs = new[] { (CostType.Material, 33.33m), (CostType.Labor, 16.67m), (CostType.Freight, 10.01m) }
            .Select(c => new QuoteItemCost { QuoteItemId = composed.Id, CostType = c.Item1, Quantity = 1, UnitCost = c.Item2, Total = c.Item2 }).ToList();

        var budget = QuoteApprovalService.Budget(180.03m + 20m, [composed, bare], costs);

        budget.Should().Equal((CostType.Material, 99.99m), (CostType.Labor, 50.01m), (CostType.Freight, 30.03m), (CostType.Other, 20m));
    }
}
