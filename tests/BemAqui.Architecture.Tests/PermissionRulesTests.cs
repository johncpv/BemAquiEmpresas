using BemAqui.BuildingBlocks.Modules;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

public class PermissionRulesTests
{
    [Fact]
    public void Codigo_so_usa_modulos_e_permissoes_do_catalogo() =>
        PermissionRules.Violations(PermissionRules.SolutionSources()).Should().BeEmpty();

    [Fact]
    public void Catalogo_de_modulos_e_consistente() =>
        PermissionRules.CatalogProblems().Should().BeEmpty();

    [Theory]
    [InlineData("""x.RequirePermission("finance.entry.approve")""")]
    [InlineData("""x.RequirePermission("finance.publish")""")]
    [InlineData("""x.RequireModule("crm")""")]
    public void Permissao_de_tres_niveis_ou_fora_do_catalogo_viola(string source) =>
        PermissionRules.Violations([("X.cs", source)]).Should().ContainSingle();

    [Theory]
    [InlineData("""x.RequirePermission("finance.pay")""")]
    [InlineData("""x.RequirePermission("hr.view_sensitive")""")]
    [InlineData("""x.RequireModule("settings")""")]
    public void Permissao_do_catalogo_e_aceita(string source) =>
        PermissionRules.Violations([("X.cs", source)]).Should().BeEmpty();

    [Fact]
    public void Catalogo_tem_os_16_modulos_aprovados_na_ADR_0010() =>
        ModuleCatalog.All.Select(m => m.Code).Should().BeEquivalentTo(
            ["dashboard", "quotes", "projects", "parties", "engineering", "production", "inventory", "purchasing", "assets", "finance", "fiscal", "hr", "kiosk", "shopfloor", "identity", "settings"]);
}
