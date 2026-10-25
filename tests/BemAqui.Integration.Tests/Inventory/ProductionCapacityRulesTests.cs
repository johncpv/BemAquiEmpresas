using BemAqui.Inventory.Application;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>EST-06 sem banco: necessidade por unidade com perda e conversão, possível, falta, item que limita, pedidos em aberto e valor da falta.</summary>
public class ProductionCapacityRulesTests
{
    [Theory]
    // quantidade por unidade, perda %, fator para a unidade de estoque → necessidade por unidade (4 casas)
    [InlineData("2", "0", "1", "2")]
    [InlineData("2", "10", "1", "2.2")]
    [InlineData("1.5", "5", "1", "1.575")]
    [InlineData("3", "0", "0.001", "0.003")] // 3 g por unidade com estoque em kg
    [InlineData("1", "12.5", "6", "6.75")] // 1 barra de 6 m com 12,5% de perda
    [InlineData("0.33333", "0", "1", "0.3333")]
    [InlineData("0.00004", "0", "1", "0")]
    public void Necessidade_por_unidade_com_perda_e_conversao(string quantity, string scrap, string factor, string expected) =>
        ProductionCapacityCalculator.PerUnit(D(quantity), D(scrap), D(factor)).Should().Be(D(expected));

    [Fact]
    public void Sem_conversao_nao_calcula() => ProductionCapacityCalculator.PerUnit(2m, 0m, null).Should().BeNull();

    [Theory]
    // necessidade por unidade, disponível, quantidade → possível, necessidade, falta
    [InlineData("2", "10", "1", "5", "2", "0")]
    [InlineData("2", "9", "1", "4", "2", "0")]
    [InlineData("2", "9", "6", "4", "12", "3")]
    [InlineData("2.2", "10", "5", "4", "11", "1")]
    [InlineData("0.003", "1", "500", "333", "1.5", "0.5")]
    [InlineData("4", "0", "2", "0", "8", "8")]
    [InlineData("1", "-3", "2", "0", "2", "2")] // disponível negativo vale zero
    public void Possivel_e_falta_por_item(string perUnit, string available, string quantity, string possible, string required, string shortage)
    {
        var result = ProductionCapacityCalculator.Calculate([new CapacityInput(D(perUnit), D(available))], D(quantity));
        var line = result.Lines.Single();
        (line.Possible, line.Required, line.Shortage).Should().Be((D(possible), D(required), D(shortage)));
        result.Possible.Should().Be(D(possible));
        result.ShortageCount.Should().Be(D(shortage) > 0 ? 1 : 0);
    }

    [Fact]
    public void Total_e_o_menor_dos_itens_e_o_item_que_limita_e_destacado()
    {
        var result = ProductionCapacityCalculator.Calculate([new CapacityInput(2m, 10m), new CapacityInput(1m, 3m), new CapacityInput(0.5m, 100m)], 4m);
        result.Possible.Should().Be(3m, "CA 1: o menor dos itens");
        result.LimitingIndex.Should().Be(1);
        result.Lines.Select(l => l.Limiting).Should().Equal(false, true, false);
        result.Lines.Select(l => l.Shortage).Should().Equal(0m, 1m, 0m);
        result.ShortageCount.Should().Be(1, "CA 2: total de itens em falta");
    }

    [Fact]
    public void Empate_marca_todos_e_o_primeiro_e_o_que_limita()
    {
        var result = ProductionCapacityCalculator.Calculate([new CapacityInput(1m, 5m), new CapacityInput(2m, 10m)], 1m);
        result.Possible.Should().Be(5m);
        result.LimitingIndex.Should().Be(0);
        result.Lines.Should().OnlyContain(l => l.Limiting);
    }

    [Fact]
    public void Item_nao_calculado_fica_fora_sem_derrubar_o_resto()
    {
        var result = ProductionCapacityCalculator.Calculate([new CapacityInput(null, 0m), new CapacityInput(2m, 7m)], 1m);
        result.Lines[0].Should().Be(new CapacityLineResult(null, null, null, null, null, false), "CA 5: \"não calculado\"");
        result.Possible.Should().Be(3m);
        result.NotCalculatedCount.Should().Be(1);
        result.LimitingIndex.Should().Be(1);
    }

    [Fact]
    public void Sem_item_calculado_nao_ha_total()
    {
        var result = ProductionCapacityCalculator.Calculate([new CapacityInput(null, 5m)], 1m);
        result.Possible.Should().BeNull();
        result.LimitingIndex.Should().BeNull();
        ProductionCapacityCalculator.Calculate([], 1m).Possible.Should().BeNull("BOM só de mão de obra: o estoque não limita");
    }

    [Fact]
    public void Pedidos_em_aberto_so_na_linha_informativa()
    {
        var without = ProductionCapacityCalculator.Calculate([new CapacityInput(2m, 4m), new CapacityInput(1m, 10m)], 5m);
        var with = ProductionCapacityCalculator.Calculate([new CapacityInput(2m, 4m, 6m), new CapacityInput(1m, 10m, 0m)], 5m);
        with.PossibleWithOpen.Should().Be(5m, "CA 3: (4 + 6) ÷ 2 = 5 e 10 ÷ 1 = 10");
        with.Possible.Should().Be(without.Possible).And.Be(2m, "ponto 92: o total continua só com o disponível de hoje");
        with.Lines.Select(l => l.Shortage).Should().Equal(without.Lines.Select(l => l.Shortage), "as faltas também");
        with.Lines[0].Shortage.Should().Be(6m);
        without.PossibleWithOpen.Should().BeNull("sem purchasing.view a linha não aparece");
    }

    [Fact]
    public void Valor_da_falta_pelo_custo_medio_em_duas_casas()
    {
        var result = ProductionCapacityCalculator.Calculate([new CapacityInput(3m, 1m, AverageCost: 12.345678m), new CapacityInput(1m, 5m, AverageCost: 2m)], 1m);
        result.Lines[0].ShortageCost.Should().Be(24.69m, "falta 2 × 12,345678 = 24,691356");
        result.Lines[1].ShortageCost.Should().Be(0m);
        result.ShortageCost.Should().Be(24.69m);
        ProductionCapacityCalculator.Calculate([new CapacityInput(3m, 1m)], 1m).ShortageCost.Should().BeNull("sem projects.view_cost");
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("2.5", true)]
    [InlineData("99999", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("100000", false)]
    [InlineData("1.00001", false)]
    public void Quantidade_desejada(string quantity, bool valid) =>
        (ProductionCapacityCalculator.ValidateQuantity(D(quantity)) is null).Should().Be(valid);

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
