using BemAqui.BuildingBlocks.Modules;
using FluentAssertions;
using NetArchTest.Rules;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// MIG-01a CA 8 e ADR 0017: a importação grava nos destinos só pelas interfaces <c>I*Import</c> dos <c>*.Contracts</c>; só o
/// módulo Migrations (e o dono, que implementa) depende delas. O Migrations fica fora do <c>ModuleCatalog</c>.
/// </summary>
public class ImportContractTests
{
    private const string ImportModule = "Migrations";

    private static readonly System.Text.RegularExpressions.Regex ImportName = new("^I[A-Z][A-Za-z]*Import$");

    [Fact]
    public void Interfaces_I_Import_ficam_nos_Contracts_e_so_o_Migrations_e_o_dono_dependem_delas()
    {
        var imports = Solution.Assemblies
            .Where(a => a.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal))
            .SelectMany(a => a.GetExportedTypes().Where(t => t.IsInterface && ImportName.IsMatch(t.Name)).Select(t => (Type: t, Owner: Solution.Classify(a.GetName().Name!).Module)))
            .ToList();

        foreach (var module in Solution.ModuleNames.Where(m => m != ImportModule))
        {
            var forbidden = imports.Where(i => i.Owner != module).Select(i => i.Type.FullName!).ToArray();
            if (forbidden.Length == 0)
            {
                continue;
            }

            var own = Solution.Assemblies.Where(a => a.GetName().Name!.StartsWith($"BemAqui.{module}.", StringComparison.Ordinal)).ToArray();
            var result = Types.InAssemblies(own).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();
            result.IsSuccessful.Should().BeTrue($"{module} usa um I*Import de outro módulo: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }

        Solution.Assemblies.Where(a => !a.GetName().Name!.EndsWith(".Contracts", StringComparison.Ordinal))
            .SelectMany(a => a.GetTypes().Where(t => t.IsInterface && ImportName.IsMatch(t.Name)))
            .Should().BeEmpty("I*Import é contrato público do módulo dono e fica no *.Contracts");
    }

    [Fact]
    public void Migrations_fica_fora_do_ModuleCatalog()
    {
        ModuleCatalog.Find("migration").Should().BeNull();
        ModuleCatalog.Find("migrations").Should().BeNull();
    }
}
