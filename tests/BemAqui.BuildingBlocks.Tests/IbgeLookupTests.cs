using BemAqui.BuildingBlocks.Lookups;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>Nome do município para achar o código IBGE: sem acento, sem caixa, sem pontuação e espaços repetidos.</summary>
public class IbgeLookupTests
{
    [Theory]
    [InlineData("Caçapava", "CACAPAVA")]
    [InlineData("São José dos Campos", "SAO JOSE DOS CAMPOS")]
    [InlineData("  são  josé dos campos ", "SÃO JOSÉ DOS CAMPOS")]
    [InlineData("Embu-Guaçu", "EMBU GUACU")]
    [InlineData("Pingo-d'Água", "Pingo d Agua")]
    public void Nomes_iguais_sem_acento_caixa_e_pontuacao_dao_a_mesma_chave(string typed, string official) =>
        BrasilApiIbgeLookup.CityKey(typed).Should().Be(BrasilApiIbgeLookup.CityKey(official));

    [Fact]
    public void Municipios_diferentes_nao_se_confundem() =>
        BrasilApiIbgeLookup.CityKey("São José").Should().NotBe(BrasilApiIbgeLookup.CityKey("São José dos Campos"));
}
