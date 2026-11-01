using BemAqui.Assets.Application;
using BemAqui.Assets.Domain;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>PAT-01b: caminho do QR (CA 2), tamanho e grade A4 (CA 1 e 3) e a etiqueta lida ou digitada (CA 7 e 9).</summary>
public class AssetLabelRulesTests
{
    [Fact]
    public void QR_leva_so_o_caminho_curto_com_a_etiqueta()
    {
        AssetLabelRules.ShortPath("PAT-00012").Should().Be("/b/PAT-00012");
        var svg = AssetLabelPdfDocument.QrSvg("https://app.konebras.com.br/b/PAT-00012");
        svg.Should().StartWith("<svg").And.Contain("shape-rendering=\"crispEdges\"").And.NotContain("PAT-00012", "o SVG só tem os módulos, nunca o texto");
    }

    [Theory]
    [InlineData(null, 50, 25)]
    [InlineData("60x30", 60, 30)]
    [InlineData("100 × 50", 100, 50)]
    [InlineData("10x10", 50, 25)]
    [InlineData("lixo", 50, 25)]
    public void Tamanho_gravado_ilegivel_ou_fora_da_faixa_vale_o_padrao(string? value, int width, int height) =>
        AssetLabelRules.ParseOrDefault(value).Should().Be(new LabelSize(width, height));

    [Fact]
    public void Faixa_do_tamanho_e_o_texto_do_parametro()
    {
        AssetLabelRules.SizeError(30, 15).Should().BeNull();
        AssetLabelRules.SizeError(150, 150).Should().BeNull();
        AssetLabelRules.SizeError(29, 25).Should().NotBeNull();
        AssetLabelRules.SizeError(50, 151).Should().NotBeNull();
        new LabelSize(50, 25).ToString().Should().Be("50x25");
    }

    [Theory]
    [InlineData(50, 25, 3, 10)]
    [InlineData(100, 50, 1, 5)]
    [InlineData(60, 30, 3, 8)]
    [InlineData(150, 150, 1, 1)]
    public void Grade_A4_pelo_tamanho_da_etiqueta(int width, int height, int columns, int rows)
    {
        var grid = AssetLabelRules.Grid(new LabelSize(width, height));
        grid.Should().Be(new LabelGrid(columns, rows));
        ((grid.Columns * width) + ((grid.Columns - 1) * AssetLabelRules.SheetGapMm)).Should().BeLessThanOrEqualTo(AssetLabelRules.SheetWidthMm - (2 * AssetLabelRules.SheetMarginMm));
        ((grid.Rows * height) + ((grid.Rows - 1) * AssetLabelRules.SheetGapMm)).Should().BeLessThanOrEqualTo(AssetLabelRules.SheetHeightMm - (2 * AssetLabelRules.SheetMarginMm));
    }

    [Theory]
    [InlineData(" pat-00012 ", "PAT-00012")]
    [InlineData("PAT-00012", "PAT-00012")]
    [InlineData("x'or'1=1", "")]
    [InlineData("PAT-00012/../../a", "")]
    [InlineData(null, "")]
    [InlineData("PAT-000000000000000012", "")]
    public void Etiqueta_lida_e_normalizada_ou_vazia(string? raw, string expected) =>
        AssetLabelRules.NormalizeTag(raw).Should().Be(expected);
}
