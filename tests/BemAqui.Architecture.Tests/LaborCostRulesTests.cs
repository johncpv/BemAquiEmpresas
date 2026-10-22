using BemAqui.Production.Application;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PRD-04 CA 10, 14 e 15 (ADR 0019 §20, ADR 0025 §2): os DTOs de apontamento sem <c>projects.view_cost</c> não têm propriedade de
/// custo (o custo vem em DTO separado, nunca em campo nulo), e só o Commercial grava a razão <c>commercial.project_costs</c>.
/// </summary>
public partial class DataOwnershipTests
{
    private static readonly string[] LaborCostWords = ["Cost", "Hourly", "Amount", "Salary", "Price"];

    [Theory]
    [InlineData(typeof(LaborEntryView))]
    [InlineData(typeof(OrderLaborView))]
    [InlineData(typeof(OpenLaborEntry))]
    [InlineData(typeof(OpenLaborList))]
    [InlineData(typeof(FabricationOrderDetail))]
    public void DTO_de_apontamento_sem_custo_nao_tem_propriedade_de_custo(Type dto) =>
        dto.GetProperties().Select(p => p.Name).Where(n => LaborCostWords.Any(w => n.Contains(w, StringComparison.Ordinal)))
            .Should().BeEmpty($"{dto.Name} é lido com production.view, sem projects.view_cost (PRD-04 CA 14)");

    [Fact]
    public void So_o_Commercial_grava_a_razao_de_custo_do_projeto()
    {
        ModuleSources().Where(s => s.Module != "Commercial" && (s.Text.Contains("ProjectCosts", StringComparison.Ordinal) || s.Text.Contains("ProjectCost ", StringComparison.Ordinal)))
            .Select(s => s.RelativePath).Should().BeEmpty("commercial.project_costs é do Commercial (ADR 0025 §2); os outros módulos publicam eventos");
        typeof(BemAqui.Commercial.Domain.ProjectCost).Assembly.GetName().Name.Should().Be("BemAqui.Commercial.Domain");
    }
}