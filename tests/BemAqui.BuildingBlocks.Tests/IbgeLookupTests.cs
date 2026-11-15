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
        EmbeddedIbgeLookup.CityKey(typed).Should().Be(EmbeddedIbgeLookup.CityKey(official));

    [Fact]
    public void Municipios_diferentes_nao_se_confundem() =>
        EmbeddedIbgeLookup.CityKey("São José").Should().NotBe(EmbeddedIbgeLookup.CityKey("São José dos Campos"));
}

/// <summary>Tabela oficial embutida: a consulta não depende de serviço externo (o servidor de produção fica fora do Brasil).</summary>
public class EmbeddedIbgeTableTests
{
    [Fact]
    public void Tabela_embutida_tem_todos_os_municipios() => EmbeddedIbgeLookup.Count.Should().BeGreaterThan(5500);

    [Theory]
    [InlineData("sp", "Cacapava", "3508504")]
    [InlineData("SP", "SÃO JOSÉ DOS CAMPOS", "3549904")]
    [InlineData("SP", "Taubaté", "3554102")]
    [InlineData("DF", "BRASILIA", "5300108")]
    [InlineData("ES", "GUACUÍ", "3202306")]
    public void Acha_o_codigo_pelo_nome_e_pela_UF(string state, string city, string code) =>
        EmbeddedIbgeLookup.Find(state, city).Should().Be(code);

    [Theory]
    [InlineData("SP", "Cidade Sem Cadastro")]
    [InlineData("RJ", "Caçapava")]
    [InlineData("", "Caçapava")]
    [InlineData("SP", " ")]
    public void Sem_municipio_com_esse_nome_na_UF_devolve_nulo(string state, string city) =>
        EmbeddedIbgeLookup.Find(state, city).Should().BeNull();
}
