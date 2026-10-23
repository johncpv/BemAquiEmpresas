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

    /// <summary>
    /// ENG-02 CA 16 e 18: a Produção liga o bloqueio das etapas sem BOM pelo <c>IBoms</c> da Engenharia (nunca o NoBomGate na composição), e
    /// o saldo da BOM fica desligado até a EST-01a.
    /// </summary>
    [Fact]
    public void Producao_bloqueia_pela_BOM_liberada_do_IBoms_da_Engenharia()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        var production = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        new BemAqui.Production.Endpoints.ProductionModule().AddServices(production, configuration);
        production.Single(d => d.ServiceType == typeof(BemAqui.Production.Application.IBomGate)).ImplementationType
            .Should().Be<BemAqui.Production.Application.EngineeringBomGate>();

        var engineering = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        new BemAqui.Engineering.Endpoints.EngineeringModule().AddServices(engineering, configuration);
        engineering.Single(d => d.ServiceType == typeof(BemAqui.Engineering.Contracts.IBoms)).ImplementationType.Should().Be<BemAqui.Engineering.Application.BomsContract>();
        engineering.Single(d => d.ServiceType == typeof(BemAqui.Engineering.Application.IBomStock)).ImplementationType
            .Should().Be<BemAqui.Engineering.Application.DisconnectedBomStock>("a EST-01a troca pelo IStockBalances");
    }

    [Fact]
    public void Contracts_do_Engineering_nao_depende_de_outro_modulo() =>
        Solution.Projects.Single(p => p.Name == "BemAqui.Engineering.Contracts").ProjectReferences
            .Select(Solution.Classify).Where(t => t.Module is not null).Should().BeEmpty();
}