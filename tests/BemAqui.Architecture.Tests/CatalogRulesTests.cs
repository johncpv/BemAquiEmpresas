using BemAqui.Catalog.Domain;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>CAT-01: dígito verificador do GTIN (CA 6), margem, markup e preço sugerido (CA 11) e as combinações da grade (CA 15).</summary>
public class CatalogRulesTests
{
    [Theory]
    [InlineData("96385074")]
    [InlineData("036000291452")]
    [InlineData("7891000315507")]
    [InlineData("17891000315504")]
    public void GTIN_8_12_13_e_14_com_digito_certo_vale(string gtin) => GtinRules.IsValid(gtin).Should().BeTrue();

    [Theory]
    [InlineData("96385075")]
    [InlineData("036000291453")]
    [InlineData("7891000315508")]
    [InlineData("17891000315505")]
    [InlineData("78910003155")]
    [InlineData("789100031550A")]
    [InlineData("")]
    public void GTIN_com_digito_errado_tamanho_fora_ou_letra_nao_vale(string gtin) => GtinRules.IsValid(gtin).Should().BeFalse();

    [Fact]
    public void Leitura_com_espaco_e_hifen_e_normalizada() => GtinRules.IsValid(GtinRules.Normalize("789 1000-315507")).Should().BeTrue();

    [Fact]
    public void Margem_sobre_o_preco_e_markup_sobre_o_custo()
    {
        MarginRules.MarginPercent(10m, 6m).Should().Be(40m);
        MarginRules.MarkupPercent(10m, 6m).Should().Be(66.67m);
        MarginRules.MarginPercent(8m, 10m).Should().Be(-25m, "preço abaixo do custo dá margem negativa");
        MarginRules.MarginPercent(null, 6m).Should().BeNull();
        MarginRules.MarkupPercent(10m, 0m).Should().BeNull("sem custo não há markup");
    }

    [Fact]
    public void Preco_sugerido_pela_margem_ou_pelo_markup_da_categoria()
    {
        MarginRules.SuggestedPrice(7m, 30m, null).Should().Be(10m);
        MarginRules.SuggestedPrice(7m, null, 30m).Should().Be(9.10m);
        MarginRules.SuggestedPrice(10m, 33m, null).Should().Be(14.93m, "arredonda para cima no centavo");
        MarginRules.SuggestedPrice(0m, 30m, null).Should().BeNull();
        MarginRules.SuggestedPrice(7m, 100m, null).Should().BeNull();
    }

    [Fact]
    public void Grade_gera_o_produto_cartesiano_na_ordem_dos_atributos()
    {
        var combos = ProductRules.Combinations([("Cor", ["Azul", "Preto", "Branco"]), ("Tamanho", ["P", "M", "G", "GG"])]);
        combos.Should().HaveCount(12);
        ProductRules.VariantName("Camiseta", combos[0]).Should().Be("Camiseta Azul P");
        ProductRules.CombinationKey(combos[0]).Should().Be(ProductRules.CombinationKey(new Dictionary<string, string> { ["cor"] = "azul", ["tamanho"] = "p" }));
    }

    [Fact]
    public void Preco_e_embalagem_respeitam_as_casas()
    {
        ProductRules.IsValidPrice(10.99m).Should().BeTrue();
        ProductRules.IsValidPrice(10.999m).Should().BeFalse();
        ProductRules.IsValidPrice(0m).Should().BeFalse();
        ProductRules.IsValidPackQuantity(12m).Should().BeTrue();
        ProductRules.IsValidPackQuantity(0.00001m).Should().BeFalse();
        ProductRules.IsValidPlu("1234").Should().BeTrue();
        ProductRules.IsValidPlu("1234567").Should().BeFalse();
    }
}
