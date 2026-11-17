using BemAqui.BuildingBlocks.Modules;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>PERF-03 (ADR 0046 §6): área "Loja", rótulo da área por perfil e vocabulário com teto de chaves.</summary>
public class BusinessVocabularyTests
{
    [Fact]
    public void Loja_fica_entre_Comercial_e_Fabrica() =>
        ModuleAreas.All.Should().Equal("Visão geral", "Comercial", "Loja", "Fábrica", "Financeiro", "Pessoas", "Telas dedicadas", "Sistema");

    // CA 5: teto de chaves por perfil e no padrão; toda chave de perfil existe no padrão (a ajuda e o front partem dele).
    [Fact]
    public void Teto_de_chaves_e_so_chaves_conhecidas()
    {
        BusinessVocabulary.Defaults.Count.Should().BeLessThanOrEqualTo(BusinessVocabulary.MaxTerms);
        foreach (var profile in BusinessProfileCatalog.All)
        {
            profile.Vocabulary.Count.Should().BeLessThanOrEqualTo(BusinessVocabulary.MaxTerms, $"o perfil {profile.Code} passou do teto (PP-25)");
            profile.Vocabulary.Keys.Should().BeSubsetOf(BusinessVocabulary.Defaults.Keys, $"o perfil {profile.Code} usa chave sem termo padrão");
        }
    }

    // CA 2, 3 e 8: sem Fábrica, "Retaguarda"; com Fábrica (com ou sem outros) ou sem perfil, nada muda.
    [Theory]
    [InlineData(new[] { "retail" }, "Retaguarda")]
    [InlineData(new[] { "services", "ecommerce" }, "Retaguarda")]
    [InlineData(new[] { "factory" }, null)]
    [InlineData(new[] { "retail", "factory" }, null)]
    [InlineData(new string[] { }, null)]
    public void Rotulo_da_area_por_perfil(string[] profiles, string? label)
    {
        var vocabulary = BusinessVocabulary.Resolve(profiles);
        var labels = BusinessVocabulary.AreaLabels(vocabulary);
        if (label is null)
        {
            vocabulary.Should().BeEmpty("a Konebras (factory) recebe o vocabulário padrão");
            labels.Should().BeEmpty();
        }
        else
        {
            labels.Should().Equal(new Dictionary<string, string> { [ModuleAreas.Factory] = label });
        }
    }

    // CA 6: a mesma chave em dois perfis segue a ordem fixa do catálogo, não a ordem de aplicação.
    [Fact]
    public void Conflito_de_chave_segue_a_ordem_fixa_do_catalogo()
    {
        var first = new BusinessProfileDefinition("a", "A", "", [], []) { Vocabulary = new Dictionary<string, string> { ["area.factory"] = "Primeiro" } };
        var second = new BusinessProfileDefinition("b", "B", "", [], []) { Vocabulary = new Dictionary<string, string> { ["area.factory"] = "Segundo" } };
        BusinessVocabulary.Resolve(["b", "a"], [first, second])["area.factory"].Should().Be("Primeiro");
        BusinessProfileCatalog.All.Select(p => p.Code).Should().Equal("factory", "retail", "services", "ecommerce");
    }

    // CA 7: a ajuda troca a marca pelo termo do tenant, cai no padrão sem termo e respeita a minúscula.
    [Fact]
    public void Marca_na_ajuda_vira_o_termo_do_tenant()
    {
        var retail = BusinessVocabulary.Resolve(["retail"]);
        BusinessVocabulary.Apply("Área {{area.factory|Fábrica}}, na {{area.factory|fábrica}}.", retail).Should().Be("Área Retaguarda, na retaguarda.");
        BusinessVocabulary.Apply("Área {{area.factory|Fábrica}}.", new Dictionary<string, string>()).Should().Be("Área Fábrica.");
        BusinessVocabulary.UnknownKeys("{{area.factory|Fábrica}} {{nao.existe|X}}").Should().Equal("nao.existe");
    }
}
