using BemAqui.Inventory.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Inventory;

/// <summary>CAD-02b CA 1, 3, 4 e 6 sem banco: conversão passando pela unidade de estoque e um arredondamento só, no fim.</summary>
public class UnitConverterTests
{
    private static readonly Guid Kg = Guid.CreateVersion7();
    private static readonly Guid M2 = Guid.CreateVersion7();
    private static readonly Guid M = Guid.CreateVersion7();

    // Estoque em KG: 1 M2 = 15,7 KG e 1 KG = 0,5 M (gravado ao contrário, da unidade de estoque para a outra).
    private static readonly ItemUnitConversion[] Conversions =
        [new() { FromUnitId = M2, ToUnitId = Kg, Factor = 15.7m }, new() { FromUnitId = Kg, ToUnitId = M, Factor = 0.5m }];

    [Theory]
    [InlineData(100, "kg", "m2", 6.3694)]
    [InlineData(31.4, "kg", "m2", 2.0000)]
    [InlineData(2, "m2", "kg", 31.4000)]
    [InlineData(10, "kg", "m", 5.0000)]
    [InlineData(1, "m2", "m", 7.8500)]
    [InlineData(3, "m", "m2", 0.3822)]
    [InlineData(7.123456, "kg", "kg", 7.123456)]
    public void Converte_pela_unidade_de_estoque(decimal quantity, string from, string to, decimal expected) =>
        UnitConverter.Convert(quantity, Unit(from), Unit(to), Kg, Conversions).Should().Be(expected);

    [Fact]
    public void Sem_conversao_nao_converte_com_fator_1() =>
        UnitConverter.Convert(1m, Guid.CreateVersion7(), Kg, Kg, Conversions).Should().BeNull();

    private static Guid Unit(string code) => code switch { "kg" => Kg, "m2" => M2, _ => M };
}
