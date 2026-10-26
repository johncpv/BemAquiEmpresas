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
        // DSH-03 CA 9: os blocos do painel do supervisor ("…Block", "SupervisorOverview") seguem a mesma regra.
        var dtos = application.GetExportedTypes().Where(t => t.Name.EndsWith("Card", StringComparison.Ordinal) || t.Name.EndsWith("Item", StringComparison.Ordinal)
            || t.Name.EndsWith("Block", StringComparison.Ordinal) || t.Name is "DashboardOverview" or "SupervisorOverview").ToList();
        dtos.Should().Contain(t => t.Name == "RevenueCard").And.Contain(t => t.Name == "CommercialCard");
        dtos.Should().Contain(t => t.Name == "SupervisorShortagesBlock").And.Contain(t => t.Name == "SupervisorQualityBlock").And.Contain(t => t.Name == "SupervisorIndicatorsBlock");

        // DSH-01b: os cards sem custo (projetos em andamento, carteira, produção, entregas) entram na mesma regra.
        dtos.Should().Contain(t => t.Name == "ActiveProjectsCard").And.Contain(t => t.Name == "BacklogCard").And.Contain(t => t.Name == "ProductionCard")
            .And.Contain(t => t.Name == "DeliveriesCard");
        dtos.Should().NotContain(IsCostDto, "um DTO com a marca [CostDto] não usa o sufixo dos cards sem custo");

        var properties = dtos.SelectMany(Reachable).Distinct().SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"{t.Name}.{p.Name}")).ToList();
        properties.Should().NotBeEmpty();
        properties.Where(p => CostWords.Any(w => p.Contains(w, StringComparison.OrdinalIgnoreCase))).Should().BeEmpty("custo e margem só com projects.view_cost, em DTO próprio (ADR 0028 §2)");
        dtos.SelectMany(Reachable).Where(IsCostDto).Should().BeEmpty("nenhum DTO sem custo carrega um com custo");
    }

    [Fact]
    public void DTOs_com_custo_do_painel_tem_a_marca_CostDto()
    {
        // DSH-01b CA 2 e 11 (ADR 0028 §2): DTO por forma de permissão. Todo tipo do painel com palavra de custo é [CostDto] (servido só pelas rotas
        // com projects.view_cost, conferido no teste de autorização), e os do card Margem e do resultado por projeto estão entre eles.
        var application = typeof(BemAqui.Dashboard.Application.DashboardService).Assembly;
        var records = application.GetExportedTypes().Where(t => t.IsClass && !t.IsAbstract && !t.IsNested && t.Namespace == "BemAqui.Dashboard.Application" && t.GetMethod("<Clone>$") is not null).ToList();
        var withCost = records.Where(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(p => CostWords.Any(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        withCost.Should().NotBeEmpty();
        withCost.Where(t => !IsCostDto(t)).Select(t => t.Name).Should().BeEmpty("tipo com custo precisa da marca [CostDto]");
        records.Where(IsCostDto).Select(t => t.Name).Should().Contain(["MarginOverview", "MarginProjectRow", "ActiveProjectMarginRow", "ProjectResultsPage", "ProjectResultDetail", "ProjectResultCell"]);
    }

    private static bool IsCostDto(Type type) => type.GetCustomAttributes(typeof(BemAqui.Dashboard.Application.CostDtoAttribute), false).Length > 0;

    [Fact]
    public void Contratos_do_painel_do_supervisor_nao_levam_custo()
    {
        // DSH-03 CA 9: os contratos novos do Production e do Purchasing que o painel compõe também não levam custo nem valor.
        Type[] types =
        [
            typeof(BemAqui.Production.Contracts.ProductionBoardSummary), typeof(BemAqui.Production.Contracts.BoardColumnSummary), typeof(BemAqui.Production.Contracts.BoardHighlightCard),
            typeof(BemAqui.Production.Contracts.QualityPendingSummary), typeof(BemAqui.Production.Contracts.QualityPendingStep), typeof(BemAqui.Production.Contracts.OpenNonConformities),
            typeof(BemAqui.Production.Contracts.OpenNonConformity), typeof(BemAqui.Production.Contracts.ProductionIndicatorsSummary), typeof(BemAqui.Production.Contracts.StoppageByReason),
            typeof(BemAqui.Production.Contracts.StoppageOrderHours), typeof(BemAqui.Production.Contracts.QualityRateByStep), typeof(BemAqui.Purchasing.Contracts.PurchaseOrderForRequestLine),
        ];
        var properties = types.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => $"{t.Name}.{p.Name}")).ToList();
        properties.Should().NotBeEmpty();
        properties.Where(p => CostWords.Concat(["Total", "Price", "Amount"]).Any(w => p.Contains(w, StringComparison.OrdinalIgnoreCase)) && !p.EndsWith(".Total", StringComparison.Ordinal) && !p.EndsWith("TotalCount", StringComparison.Ordinal))
            .Should().BeEmpty("o painel do supervisor não mostra custo nem valor (ADR 0028 §2)");
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
