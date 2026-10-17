using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>Prova de que as regras falham quando o acoplamento proibido é forçado.</summary>
public class RulesTests
{
    private static ProjectInfo Project(string name, string[]? refs = null, string[]? packages = null, string[]? frameworks = null)
    {
        var (module, layer) = Solution.Classify(name);
        return new ProjectInfo(name, module, layer, refs ?? [], packages ?? [], frameworks ?? []);
    }

    [Theory]
    [InlineData("BemAqui.Inventory.Domain")]
    [InlineData("BemAqui.Inventory.Application")]
    [InlineData("BemAqui.Inventory.Infrastructure")]
    [InlineData("BemAqui.Inventory.Endpoints")]
    public void Referencia_a_camada_interna_de_outro_modulo_viola(string target)
    {
        var finance = Project("BemAqui.Finance.Application", [target]);

        Rules.CrossModuleReferences([finance]).Should().ContainSingle().Which.Should().Contain(target);
    }

    [Fact]
    public void Referencia_a_Contracts_de_outro_modulo_e_permitida()
    {
        var finance = Project("BemAqui.Finance.Application", ["BemAqui.Inventory.Contracts", "BemAqui.Finance.Domain", "BemAqui.BuildingBlocks"]);

        Rules.CrossModuleReferences([finance]).Should().BeEmpty();
    }

    [Fact]
    public void Contracts_referenciando_Domain_viola()
    {
        var contracts = Project("BemAqui.Finance.Contracts", ["BemAqui.Finance.Domain"]);

        Rules.ContractsReferences([contracts]).Should().ContainSingle();
    }

    [Theory]
    [InlineData("MediatR")]
    [InlineData("AutoMapper")]
    [InlineData("AutoMapper.Extensions.Microsoft.DependencyInjection")]
    public void MediatR_e_AutoMapper_violam(string package) =>
        Rules.ForbiddenPackages([Project("BemAqui.Host", packages: [package])]).Should().ContainSingle();

    [Fact]
    public void Pacote_com_nome_parecido_nao_e_falso_positivo() =>
        Rules.ForbiddenPackages([Project("BemAqui.Host", packages: ["MediatorKit", "AutoMapperless"])]).Should().BeEmpty();

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Npgsql")]
    public void Domain_com_pacote_de_infraestrutura_viola(string package) =>
        Rules.DomainInfrastructure([Project("BemAqui.Finance.Domain", packages: [package])]).Should().ContainSingle();

    [Fact]
    public void Domain_com_framework_AspNetCore_viola() =>
        Rules.DomainInfrastructure([Project("BemAqui.Finance.Domain", frameworks: ["Microsoft.AspNetCore.App"])]).Should().ContainSingle();
}
