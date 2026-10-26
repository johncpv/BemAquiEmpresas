using System.Reflection;
using BemAqui.Dashboard.Endpoints;
using BemAqui.Host;
using BemAqui.Host.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NetArchTest.Rules;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// DSH-01a CA 1 e CA 15 (ADR 0028 §1 e §2): o <c>Dashboard</c> é módulo de composição. Só referencia <c>*.Contracts</c> e o
/// BuildingBlocks, não tem <c>DbSet</c>, Dapper, <c>TenantDbContext</c>, migration nem schema, e os DTOs dos cards desta story
/// não levam custo, orçado nem margem.
/// </summary>
public class CompositionRulesTests
{
    private const string Module = "Dashboard";

    private static readonly string[] CostWords = ["Cost", "Margin", "Budget", "Markup", "Salary", "HourlyRate"];

    [Fact]
    public void Dashboard_so_tem_Application_e_Endpoints_e_so_referencia_Contracts()
    {
        Solution.CompositionModules.Should().Contain(Module);
        CompositionRules.ProjectViolations(Solution.Projects, Module).Should().BeEmpty();
        CompositionRules.ProjectViolations([new ProjectInfo("BemAqui.Dashboard.Infrastructure", Module, "Infrastructure", ["BemAqui.Finance.Application"], ["Dapper"], [])], Module)
            .Should().HaveCount(3, "a regra acusa camada, pacote e referência proibidos");
    }

    [Fact]
    public void Dashboard_nao_tem_DbSet_Dapper_TenantDbContext_migration_nem_schema()
    {
        CompositionRules.SourceViolations(CompositionRules.ModuleSources(Module)).Should().BeEmpty();
        CompositionRules.SourceViolations([("X.cs", "public DbSet<Card> Cards => Set<Card>();"), ("Y.cs", "await connection.QueryAsync<Card>(sql);"), ("Z.cs", "b.ToTable(\"cards\", \"dashboard\");")])
            .Should().HaveCount(3, "a regra acusa o caso proibido");
    }

    [Fact]
    public void Tipos_do_Dashboard_nao_dependem_de_EF_Npgsql_Dapper_nem_do_Host()
    {
        // Pelos tipos (contexto padrão), não pelo arquivo: roda também com o Smart App Control do Windows ligado.
        var assemblies = new[] { typeof(BemAqui.Dashboard.Application.DashboardService).Assembly, typeof(DashboardModule).Assembly };
        assemblies.Select(a => a.GetName().Name).Should().BeEquivalentTo("BemAqui.Dashboard.Application", "BemAqui.Dashboard.Endpoints");

        var result = Types.InAssemblies(assemblies).ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Dapper", "BemAqui.Host").GetResult();
        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Dashboard_nao_tem_entidade_no_TenantDbContext_nem_assembly_de_persistencia()
    {
        ModuleRegistry.All.Where(m => m is DashboardModule).Should().ContainSingle().Which.PersistenceAssembly.Should().BeNull();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseNpgsql("Host=localhost").UseSnakeCaseNamingConvention().Options;
        using var db = new TenantDbContext(options);
        db.Model.GetEntityTypes().Select(t => t.ClrType.Assembly.GetName().Name).Should().NotContain(n => n!.StartsWith("BemAqui.Dashboard.", StringComparison.Ordinal));
        db.Model.GetEntityTypes().Select(t => t.GetSchema()).Should().NotContain("dashboard");
    }

    [Fact]
    public void DTOs_dos_cards_nao_levam_custo_orcado_nem_margem()
    {
        var application = typeof(BemAqui.Dashboard.Application.DashboardService).Assembly;
        var dtos = application.GetExportedTypes().Where(t => t.Name.EndsWith("Card", StringComparison.Ordinal) || t.Name.EndsWith("Item", StringComparison.Ordinal) || t.Name == "DashboardOverview").ToList();
        dtos.Should().Contain(t => t.Name == "RevenueCard").And.Contain(t => t.Name == "CommercialCard");

        var properties = dtos.SelectMany(Reachable).Distinct().SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"{t.Name}.{p.Name}")).ToList();
        properties.Should().NotBeEmpty();
        properties.Where(p => CostWords.Any(w => p.Contains(w, StringComparison.OrdinalIgnoreCase))).Should().BeEmpty("custo e margem são da DSH-01b, com projects.view_cost (ADR 0028 §2)");
    }

    /// <summary>O tipo e os tipos do próprio painel que ele carrega (listas e registros aninhados).</summary>
    private static IEnumerable<Type> Reachable(Type type)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([type]);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current))
            {
                continue;
            }

            foreach (var property in current.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var target = property.PropertyType.IsGenericType ? property.PropertyType.GetGenericArguments()[0] : property.PropertyType;
                if (target.Assembly == type.Assembly)
                {
                    pending.Push(target);
                }
            }
        }

        return seen;
    }
}
