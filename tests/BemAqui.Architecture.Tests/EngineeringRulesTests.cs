using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>ENG-01 (ADR 0006): o Engineering só conversa com a Produção (centros) e o Estoque (produtos) pelos Contracts.</summary>
public class EngineeringRulesTests
{
    [Fact]
    public void Engineering_so_referencia_os_Contracts_da_Producao_e_do_Estoque()
    {
        var others = Solution.Projects.Where(p => p.Module == "Engineering")
            .SelectMany(p => p.ProjectReferences.Select(Solution.Classify))
            .Where(t => t.Module is not null && t.Module != "Engineering")
            .Select(t => $"BemAqui.{t.Module}.{t.Layer}")
            .Distinct()
            .ToList();
        others.Should().BeEquivalentTo(["BemAqui.Production.Contracts", "BemAqui.Inventory.Contracts"]);
    }

    [Fact]
    public void Contracts_do_Engineering_nao_depende_de_outro_modulo() =>
        Solution.Projects.Single(p => p.Name == "BemAqui.Engineering.Contracts").ProjectReferences
            .Select(Solution.Classify).Where(t => t.Module is not null).Should().BeEmpty();
}