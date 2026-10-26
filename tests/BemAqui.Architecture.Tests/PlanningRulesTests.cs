using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using NetArchTest.Rules;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PLN-01a (ADR 0029): o <see cref="ScheduleService"/> é o único escritor das tabelas do plano (CA 11); o plano fica fora da auditoria e
/// nunca toca o documento da OS (CA 10); as rotas do Planejamento ficam no grupo do módulo com <c>perm:</c> (CA 18); o job recorrente é
/// registrado no fuso de São Paulo e confere o módulo liberado (CA 13).
/// </summary>
public class PlanningRulesTests
{
    private static string Production => Path.Combine(Solution.RootDirectory, "src", "Modules", "Production");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Production, .. parts]));

    [Fact]
    public void So_o_ScheduleService_depende_do_escritor_do_plano()
    {
        // Os outros módulos só enxergam os Contracts do Production (regra de módulos), então o escritor só pode aparecer nestes assemblies.
        var production = new[] { typeof(ScheduleService).Assembly, typeof(BemAqui.Production.Infrastructure.ProductionInfrastructure).Assembly, typeof(BemAqui.Production.Endpoints.ProductionModule).Assembly };
        var writers = Types.InAssemblies(production).That().HaveDependencyOn(typeof(IProductionPlanDb).FullName).GetTypes()
            .Select(t => t.FullName!).Where(n => !n.StartsWith(typeof(ScheduleService).FullName!, StringComparison.Ordinal)).ToList();
        writers.Should().BeEmpty("CA 11: só o ScheduleService grava schedule_runs, schedule_allocations e schedule_step_forecasts");

        var sources = Directory.GetFiles(Path.Combine(Solution.RootDirectory, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("IProductionPlanDb", StringComparison.Ordinal))
            .Select(Path.GetFileName).Order().ToList();
        sources.Should().Contain("ScheduleService.cs").And.BeSubsetOf(["IProductionPlanDb.cs", "ScheduleService.cs", "TenantDbContext.cs", "TenantPersistence.cs"],
            "o escritor só aparece na definição, no escritor e no DbContext");
    }

    [Fact]
    public void Leitura_do_plano_e_sem_rastreamento()
    {
        typeof(IProductionDb).GetProperty(nameof(IProductionDb.ScheduleRunsView))!.PropertyType.Should().Be<IQueryable<ScheduleRun>>("quem lê não consegue gravar");
        typeof(IProductionDb).GetProperty(nameof(IProductionDb.ScheduleAllocationsView))!.PropertyType.Should().Be<IQueryable<ScheduleAllocation>>();
        typeof(IProductionDb).GetProperty(nameof(IProductionDb.ScheduleStepForecastsView))!.PropertyType.Should().Be<IQueryable<ScheduleStepForecast>>();
        File.ReadAllText(Path.Combine(Solution.RootDirectory, "src", "Host", "BemAqui.Host", "Persistence", "TenantDbContext.cs"))
            .Should().Contain("IProductionDb.ScheduleStepForecastsView => Set<ScheduleStepForecast>().AsNoTracking()");
    }

    [Theory]
    [InlineData(typeof(ScheduleRun))]
    [InlineData(typeof(ScheduleAllocation))]
    [InlineData(typeof(ScheduleStepForecast))]
    public void Tabelas_do_plano_ficam_fora_da_auditoria(Type type)
    {
        typeof(IAuditable).IsAssignableFrom(type).Should().BeFalse("CA 10, ADR 0029: o histórico do plano é a schedule_runs");
        Read("BemAqui.Production.Infrastructure", "ProductionInfrastructure.cs").Should().NotContain($"AddAuditableEntity<{type.Name}>");
    }

    [Fact]
    public void O_escritor_nao_toca_a_OS_nem_a_etapa()
    {
        var writer = Read("BemAqui.Production.Application", "ScheduleService.cs");
        writer.Should().NotContainAny(["ProductionOrders", "ProductionOrderSteps", "IProductionDb"], "CA 10: nenhuma linha de production_orders nem de production_order_steps");
        writer.Should().Contain("pg_try_advisory_xact_lock").And.Contain("BeginTransactionAsync", "CA 10 e 12: uma transação e o lock por tenant");
        Read("BemAqui.Production.Application", "ScheduleInputBuilder.cs").Should().Contain("AsNoTracking()").And.NotContain("SaveChanges");
        Read("BemAqui.Production.Application", "ProductionScheduleContract.cs").Should().NotContainAny(["SaveChanges", "IProductionPlanDb"], "CA 15: a simulação não grava");
    }

    [Fact]
    public void Rotas_do_planejamento_tem_modulo_e_permissao()
    {
        Read("BemAqui.Production.Endpoints", "ProductionEndpoints.cs").Should().Contain("MapSchedule(production.MapGroup(\"/schedule\"))", "RequireModule no grupo /production");
        var endpoints = Read("BemAqui.Production.Endpoints", "ProductionEndpoints.Planning.cs");
        foreach (var (name, permission) in new[] { ("GetPlanningSummary", "view"), ("ListOrderForecasts", "view"), ("RecalculateSchedule", "edit") })
        {
            endpoints.Split(';').Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal)).Should().Contain($"RequirePermission(\"production.{permission}\")", name);
        }
    }

    [Fact]
    public void Job_de_hora_em_hora_confere_o_modulo_e_fica_no_registrar()
    {
        var registrar = File.ReadAllText(Path.Combine(Solution.RootDirectory, "src", "Host", "BemAqui.Host", "RecurringJobsRegistrar.cs"));
        registrar.Should().Contain("PlanningRecalculateRecurringJob.Id").And.Contain("PlanningRecalculateRecurringJob.Cron");
        PlanningRecalculateRecurringJob.Cron.Should().Be("0 * * * *");
        var job = Read("BemAqui.Production.Application", "PlanningRecalculateJob.cs");
        job.Split("IsReleased(PlanningService.Module)").Length.Should().Be(3, "CA 13: o recorrente filtra e o job confere de novo no início");
        typeof(PlanningRecalculateArgs).GetProperties().Select(p => p.PropertyType).Should().NotContain(typeof(Guid), "só o tenantId do job identifica o tenant");
    }
}
