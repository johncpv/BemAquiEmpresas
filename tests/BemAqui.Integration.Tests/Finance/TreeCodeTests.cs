using BemAqui.Finance.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>CAD-05a CA 5 e 6 sem banco: máscara 9.9.99, próximo código do nível e ordem por segmento.</summary>
public class TreeCodeTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("2.1", true)]
    [InlineData("2.1.03", true)]
    [InlineData("2.1.3", false)]
    [InlineData("0", false)]
    [InlineData("2.10", false)]
    [InlineData("2.1.00", false)]
    [InlineData("2.1.03.01", false)]
    [InlineData("A", false)]
    public void Mascara(string code, bool valid) => TreeCode.IsValid(code).Should().Be(valid);

    [Fact]
    public void Proximo_codigo_do_nivel()
    {
        TreeCode.Next("2.1", ["2.1.01", "2.1.03"]).Should().Be("2.1.04");
        TreeCode.Next("2.1", []).Should().Be("2.1.01");
        TreeCode.Next("3", ["3.1", "3.9"]).Should().BeNull("o nível 2 só vai até 9");
        TreeCode.Next(null, ["1", "2", "5"]).Should().Be("6");
        TreeCode.Next("2.1.03", []).Should().BeNull("só há três níveis");
    }

    [Fact]
    public void Ordena_por_segmento() =>
        new[] { "2.1.10", "2.1.09", "10", "2" }.OrderBy(TreeCode.SortKey).Should().Equal("2", "2.1.09", "2.1.10", "10");
}
