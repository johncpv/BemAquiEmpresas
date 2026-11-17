using BemAqui.BuildingBlocks.Modules;
using FluentAssertions;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>PERF-01 (ADR 0046): liberação efetiva por plano, perfil e exceção, e os três estados do menu.</summary>
public class BusinessProfileCatalogTests
{
    // Tabela-verdade completa: plano × perfil × exceção manual → liberado e estado no menu.
    [Theory]
    [InlineData(true, true, null, true, ModuleVisibility.Released)]
    [InlineData(true, false, null, false, ModuleVisibility.Hidden)]
    [InlineData(false, true, null, false, ModuleVisibility.Locked)]
    [InlineData(false, false, null, false, ModuleVisibility.Hidden)]
    [InlineData(true, true, false, false, ModuleVisibility.Locked)]
    [InlineData(true, false, true, true, ModuleVisibility.Released)]
    [InlineData(false, true, true, true, ModuleVisibility.Released)]
    [InlineData(false, false, true, true, ModuleVisibility.Released)]
    [InlineData(false, false, false, false, ModuleVisibility.Hidden)]
    public void Liberacao_efetiva_e_estado(bool inPlan, bool inProfile, bool? exception, bool released, ModuleVisibility state)
    {
        var effective = exception ?? BusinessProfileCatalog.Inherited(inPlan, inProfile);
        effective.Should().Be(released);
        BusinessProfileCatalog.Visibility(effective, inProfile).Should().Be(state);
    }

    [Fact]
    public void Perfis_combinam_por_uniao_e_sem_perfil_nao_ha_filtro()
    {
        var union = BusinessProfileCatalog.ModulesOf([BusinessProfileCatalog.Retail, BusinessProfileCatalog.Ecommerce])!;
        union.Should().Contain("kiosk").And.NotContain("production");
        BusinessProfileCatalog.ModulesOf([]).Should().BeNull();
        BusinessProfileCatalog.InProfile(null, "production").Should().BeTrue();
        BusinessProfileCatalog.Hidden([], new HashSet<string>()).Should().BeEmpty("tenant sem perfil mantém o cadeado de antes");
    }

    [Fact]
    public void Fabrica_lista_exatamente_os_modulos_que_a_Konebras_tem_hoje() =>
        BusinessProfileCatalog.Find(BusinessProfileCatalog.Factory)!.Modules.Should().BeEquivalentTo(
            ["dashboard", "quotes", "projects", "parties", "engineering", "production", "inventory", "purchasing", "assets", "finance", "fiscal", "hr", "kiosk", "shopfloor", "identity", "settings"]);

    [Fact]
    public void Perfis_e_segmentos_declarados()
    {
        BusinessProfileCatalog.All.Select(p => p.Code).Should().Equal("factory", "retail", "services", "ecommerce");
        BusinessProfileCatalog.Find("retail")!.Segments.Select(s => s.Code).Should().Equal("counter", "supermarket", "bakery", "restaurant", "snack_bar", "food_truck");
        BusinessProfileCatalog.Find("services")!.Segments.Select(s => s.Code).Should().Equal("workshop");
        BusinessProfileCatalog.IsKnown("retail", "workshop").Should().BeFalse();
    }
}
