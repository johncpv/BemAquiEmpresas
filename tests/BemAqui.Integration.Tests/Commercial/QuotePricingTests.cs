using BemAqui.Commercial.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>COM-01a CA 12 e 15: preço sugerido, rótulos equivalentes e totais com o arredondamento da ADR 0019 §9.</summary>
public class QuotePricingTests
{
    [Fact]
    public void Markup_e_margem_do_exemplo_da_story()
    {
        QuotePricing.SuggestedPrice(1_000m, QuotePricingMode.Markup, 40m).Should().Be(1_400m);
        QuotePricing.MarginFromMarkup(40m).Should().Be(28.5714m);
        QuotePricing.SuggestedPrice(1_000m, QuotePricingMode.Margin, 40m).Should().Be(1_666.67m);
        QuotePricing.MarkupFromMargin(40m).Should().Be(66.6667m);
        QuotePricing.SuggestedPrice(1_000m, QuotePricingMode.Margin, 100m).Should().BeNull("margem de 100% não tem preço");
    }

    [Fact]
    public void Preco_digitado_recalcula_o_percentual_e_os_totais_arredondam_por_produto()
    {
        QuotePricing.PctOf(1_000m, 1_250m, QuotePricingMode.Markup).Should().Be(25m);
        QuotePricing.PctOf(1_000m, 1_250m, QuotePricingMode.Margin).Should().Be(20m);
        QuotePricing.LineTotal(3.3333m, 1.005m).Should().Be(3.35m);
        var (cost, price, margin) = QuotePricing.Totals([(2m, 100.005m, 150m), (1.5m, 33.333333m, 40m)]);
        cost.Should().Be(200.01m + 50m);
        price.Should().Be(360m);
        margin.Should().Be(30.5528m);
        QuotePricing.Totals([(1m, 10m, 0m)]).Margin.Should().BeNull();
        QuotePricing.Totals([(1m, 100m, 80m)]).Margin.Should().Be(-25m, "margem negativa não bloqueia");
    }
}
