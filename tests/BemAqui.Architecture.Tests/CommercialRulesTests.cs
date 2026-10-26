using BemAqui.Commercial.Application;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>COM-01a CA 2 e 10 (D7): DTO sem custo não tem propriedade de custo; o custo de mão de obra é sempre do cargo.</summary>
public class CommercialRulesTests
{
    private static readonly string[] CostWords = ["Cost", "Margin", "Markup", "Pct", "Hourly"];

    [Theory]
    [InlineData(typeof(QuoteSummary))]
    [InlineData(typeof(QuoteDetail))]
    [InlineData(typeof(QuoteItemView))]
    [InlineData(typeof(QuotePage))]
    [InlineData(typeof(ProjectListRow))]
    [InlineData(typeof(ProjectListPage))]
    [InlineData(typeof(ProjectDetail))]
    [InlineData(typeof(ProjectFinancial))]
    [InlineData(typeof(BemAqui.Commercial.Contracts.ProjectMarginHeader))]
    [InlineData(typeof(BemAqui.Commercial.Contracts.ProjectSummary))]
    public void DTO_sem_custo_nao_tem_propriedade_de_custo(Type dto) =>
        dto.GetProperties().Select(p => p.Name).Where(n => CostWords.Any(w => n.Contains(w, StringComparison.Ordinal)) || n.Contains("Budget", StringComparison.Ordinal))
            .Should().BeEmpty($"{dto.Name} é o DTO de quem não tem quotes.view_cost nem projects.view_cost (ADR 0019 §20, DSH-02 CA 19)");

    /// <summary>
    /// DSH-02 CA 14 (§4.3 da revisão): o drill-down de horas sem <c>hr.view_sensitive</c> não tem nome nem custo-hora individual; isso só
    /// existe no DTO de horas por funcionário, de rota própria que exige a ação.
    /// </summary>
    [Theory]
    [InlineData(typeof(ProjectCostEntry))]
    [InlineData(typeof(ProjectCostEntries))]
    public void Drill_down_sem_dado_sensivel_nao_tem_nome_nem_custo_hora(Type dto) =>
        dto.GetProperties().Select(p => p.Name).Where(n => n.Contains("Worker", StringComparison.Ordinal) || n.Contains("Hourly", StringComparison.Ordinal)
                || n.Contains("Employee", StringComparison.Ordinal) || n == "Name")
            .Should().BeEmpty($"{dto.Name} é lido só com projects.view_cost (DSH-02 CA 14)");

    [Fact]
    public void Horas_por_funcionario_tem_nome_e_custo_hora_em_DTO_proprio() =>
        typeof(ProjectLaborWorkerEntry).GetProperties().Select(p => p.Name).Should().Contain(["WorkerName", "HourlyCost"]);

    /// <summary>DSH-02 CA 1: um cálculo só, no Domain do Commercial; a aba Custos, o relatório e o <c>IProjectMargins</c> passam por ele.</summary>
    [Fact]
    public void Um_calculo_so_de_margem_no_Domain() =>
        typeof(BemAqui.Commercial.Domain.ProjectMarginCalculator).Assembly.GetName().Name.Should().Be("BemAqui.Commercial.Domain");

    [Fact]
    public void Commercial_nao_usa_custo_por_funcionario()
    {
        var module = Path.Combine(Solution.RootDirectory, "src", "Modules", "Commercial");
        var sep = Path.DirectorySeparatorChar;
        Directory.GetFiles(module, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}", StringComparison.Ordinal) && !f.Contains($"{sep}bin{sep}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("IEmployees", StringComparison.Ordinal) || File.ReadAllText(f).Contains("employeeId", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("o custo-hora do orçamento é sempre do cargo (CAD-04a CA 8)");
    }
}
