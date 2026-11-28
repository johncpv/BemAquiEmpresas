using BemAqui.BuildingBlocks.Modules;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>PERF-04: quais pacotes de dados iniciais rodam para cada perfil e segmento.</summary>
public class BusinessProfileSeedsTests
{
    [Fact]
    public void Fabrica_e_tenant_sem_perfil_nao_rodam_pacote_nenhum()
    {
        BusinessProfileSeeds.PackagesFor([BusinessProfileCatalog.Factory], []).Should().BeEmpty("CA 5: a Konebras não ganha nem muda nada");
        BusinessProfileSeeds.PackagesFor([], []).Should().BeEmpty();
        BusinessProfileSeeds.ByProfile[BusinessProfileCatalog.Factory].Should().BeEmpty();
    }

    [Fact]
    public void Comercio_roda_os_pacotes_de_cada_modulo_com_o_terceiro_antes_das_formas()
    {
        var packages = BusinessProfileSeeds.PackagesFor([BusinessProfileCatalog.Retail], ["counter"]);
        packages.Should().Equal(BusinessProfileSeeds.PartiesRetail, BusinessProfileSeeds.FinanceRetail, BusinessProfileSeeds.InventoryRetail,
            BusinessProfileSeeds.IdentityRetail, BusinessProfileSeeds.CatalogRetail, BusinessProfileSeeds.PosRetail);
    }

    [Fact]
    public void Segmento_de_alimentacao_acrescenta_as_unidades_e_a_uniao_nao_repete_pacote()
    {
        BusinessProfileSeeds.PackagesFor([BusinessProfileCatalog.Retail], ["bakery"]).Should().EndWith(BusinessProfileSeeds.InventoryFood);
        BusinessProfileSeeds.PackagesFor([BusinessProfileCatalog.Retail], ["supermarket"]).Should().NotContain(BusinessProfileSeeds.InventoryFood);
        BusinessProfileSeeds.PackagesFor([BusinessProfileCatalog.Ecommerce, BusinessProfileCatalog.Retail, BusinessProfileCatalog.Factory], ["bakery", "snack_bar"])
            .Should().OnlyHaveUniqueItems().And.HaveCount(7);
        // Segmento de outro perfil não vale: o Serviços com "bakery" roda só o pacote do próprio perfil (EXE-02).
        BusinessProfileSeeds.PackagesFor([BusinessProfileCatalog.Services], ["bakery"]).Should().Equal(BusinessProfileSeeds.ProjectsServices);
    }

    [Fact]
    public void Todo_perfil_e_segmento_de_alimentacao_existem_no_catalogo()
    {
        BusinessProfileSeeds.ByProfile.Keys.Should().BeEquivalentTo(BusinessProfileCatalog.All.Select(p => p.Code));
        BusinessProfileSeeds.FoodSegments.Should().OnlyContain(s => BusinessProfileCatalog.IsKnown(BusinessProfileCatalog.Retail, s));
    }
}
