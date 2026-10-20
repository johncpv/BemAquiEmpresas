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
    public void DTO_sem_custo_nao_tem_propriedade_de_custo(Type dto) =>
        dto.GetProperties().Select(p => p.Name).Where(n => CostWords.Any(w => n.Contains(w, StringComparison.Ordinal)))
            .Should().BeEmpty($"{dto.Name} é o DTO de quem não tem quotes.view_cost");

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
