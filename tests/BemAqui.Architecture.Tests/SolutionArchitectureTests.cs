using FluentAssertions;
using NetArchTest.Rules;

namespace BemAqui.Architecture.Tests;

/// <summary>Regras aplicadas à solução real (FND-01, CA 3 a 6).</summary>
public class SolutionArchitectureTests
{
    private static readonly string[] DomainForbiddenNamespaces = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql"];

    [Fact]
    public void Existem_os_13_modulos_cada_um_com_as_5_camadas()
    {
        Solution.ModuleNames.Should().BeEquivalentTo(
            "Platform", "Identity", "Parties", "HR", "Commercial", "Engineering", "Production",
            "Inventory", "Purchasing", "Finance", "Fiscal", "Assets", "Portal");

        foreach (var module in Solution.ModuleNames)
        {
            Solution.Projects.Where(p => p.Module == module).Select(p => p.Layer)
                .Should().BeEquivalentTo(Solution.ModuleLayers, $"o módulo {module} deve ter as 5 camadas");
        }
    }

    [Fact]
    public void Modulo_so_referencia_Contracts_de_outro_modulo() =>
        Rules.CrossModuleReferences(Solution.Projects).Should().BeEmpty();

    [Fact]
    public void Contracts_so_depende_de_Contracts() =>
        Rules.ContractsReferences(Solution.Projects).Should().BeEmpty();

    [Fact]
    public void Nenhum_projeto_usa_MediatR_ou_AutoMapper()
    {
        Rules.ForbiddenPackages(Solution.Projects).Should().BeEmpty();

        var central = File.ReadAllText(Path.Combine(Solution.RootDirectory, "Directory.Packages.props"));
        central.Should().NotContainAny("\"MediatR", "\"AutoMapper");

        Solution.Assemblies
            .SelectMany(a => a.GetReferencedAssemblies().Select(r => (Assembly: a.GetName().Name, Reference: r.Name ?? string.Empty)))
            .Where(x => Rules.IsForbiddenLibrary(x.Reference))
            .Should().BeEmpty();
    }

    [Fact]
    public void Domain_nao_referencia_infraestrutura_no_csproj() =>
        Rules.DomainInfrastructure(Solution.Projects).Should().BeEmpty();

    [Fact]
    public void Tipos_do_Domain_nao_dependem_de_infraestrutura()
    {
        var domains = Solution.Assemblies.Where(a => a.GetName().Name!.EndsWith(".Domain", StringComparison.Ordinal)).ToArray();
        domains.Should().HaveCount(Solution.ModuleNames.Count);

        var result = Types.InAssemblies(domains).ShouldNot().HaveDependencyOnAny(DomainForbiddenNamespaces).GetResult();

        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Tipos_de_um_modulo_so_dependem_de_Contracts_de_outro_modulo()
    {
        foreach (var module in Solution.ModuleNames)
        {
            var own = Solution.Assemblies.Where(a => a.GetName().Name!.StartsWith($"BemAqui.{module}.", StringComparison.Ordinal)).ToArray();
            var forbidden = Solution.ModuleNames
                .Where(m => m != module)
                .SelectMany(m => new[] { "Domain", "Application", "Infrastructure", "Endpoints" }.Select(l => $"BemAqui.{m}.{l}"))
                .ToArray();

            var result = Types.InAssemblies(own).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

            result.IsSuccessful.Should().BeTrue($"{module}: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }
}
