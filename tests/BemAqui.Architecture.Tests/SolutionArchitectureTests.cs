using FluentAssertions;
using NetArchTest.Rules;

namespace BemAqui.Architecture.Tests;

/// <summary>Regras aplicadas à solução real (FND-01, CA 3 a 6).</summary>
public class SolutionArchitectureTests
{
    private static readonly string[] DomainForbiddenNamespaces = ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql"];

    /// <summary>
    /// 15 módulos: 14 com as 5 camadas e o <c>Dashboard</c>, módulo de composição só com Application e Endpoints (ADR 0028 §1,
    /// DSH-01a CA 1; as regras dele estão em <see cref="CompositionRulesTests"/>).
    /// </summary>
    [Fact]
    public void Existem_os_15_modulos_com_as_5_camadas_e_o_Dashboard_so_de_composicao()
    {
        Solution.ModuleNames.Should().BeEquivalentTo(
            "Platform", "Identity", "Parties", "HR", "Commercial", "Engineering", "Production",
            "Inventory", "Purchasing", "Finance", "Fiscal", "Assets", "Portal", "Migrations", "Dashboard");

        foreach (var module in Solution.ModuleNames)
        {
            var expected = Solution.CompositionModules.Contains(module) ? Solution.CompositionLayers : Solution.ModuleLayers;
            Solution.Projects.Where(p => p.Module == module).Select(p => p.Layer)
                .Should().BeEquivalentTo(expected, $"o módulo {module} deve ter as camadas {string.Join(", ", expected)}");
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
        domains.Should().HaveCount(Solution.ModuleNames.Count - Solution.CompositionModules.Count);

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
