using BemAqui.Commercial.Domain;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PLN-01b (ADR 0029 D10): os comandos que mudam as datas pedem o recálculo (CA 1) e os consumidores de aditivo e cancelamento também
/// (CA 2); a previsão do projeto fica fora do documento e da auditoria, com um escritor só no Commercial (CA 7); o job do pedido por evento
/// só recebe o tenantId (CA 13); o evento leva só ids, datas e código (CA 14).
/// </summary>
public class PlanningEventRulesTests
{
    private static string Src => Path.Combine(Solution.RootDirectory, "src");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Src, .. parts]));

    private static string Production(string file) => Read("Modules", "Production", "BemAqui.Production.Application", file);

    private static string Commercial(string file) => Read("Modules", "Commercial", "BemAqui.Commercial.Application", file);

    [Theory]
    [InlineData("ProductionBoardNotifier.cs")]
    [InlineData("CalendarService.cs")]
    [InlineData("WorkCenterService.cs")]
    [InlineData("ProductionParametersService.cs")]
    [InlineData("ScheduleTriggers.cs")]
    public void Comandos_que_mudam_as_datas_pedem_o_recalculo(string file) =>
        Production(file).Should().Contain("trigger.RequestAsync(", "CA 1 e 2: o pedido sai depois do commit pelo IScheduleTrigger");

    [Fact]
    public void Pedido_sai_depois_do_commit_e_confere_o_modulo()
    {
        var trigger = Production("ScheduleTriggers.cs");
        trigger.Should().Contain("afterCommit.RunAsync(").And.Contain("IsReleased(PlanningService.Module)").And.Contain("CreateScopeAsync(info");
        trigger.Should().Contain("new PlanningRecalculateArgs(ScheduleTrigger.Event)").And.Contain("j.ExecuteAsync(info.Id, args");
        typeof(PlanningRecalculateArgs).GetProperties().Select(p => p.PropertyType).Should().Equal([typeof(ScheduleTrigger)], "CA 13: só o tenantId do job identifica o tenant");
    }

    [Fact]
    public void Consumidores_registrados_no_processo()
    {
        var events = Read("Host", "BemAqui.Host", "HostEvents.cs");
        events.Should().Contain("ContractAmended, BemAqui.Production.Application.PlanningContractConsumers>()")
            .And.Contain("ContractCancelled, BemAqui.Production.Application.PlanningContractConsumers>()")
            .And.Contain("ProjectForecastChanged, BemAqui.Commercial.Application.ProjectForecastConsumer>()");
    }

    [Theory]
    [InlineData(typeof(ScheduleProjectForecast))]
    [InlineData(typeof(ProjectForecast))]
    public void Previsao_do_projeto_fora_da_auditoria(Type type)
    {
        typeof(IAuditable).IsAssignableFrom(type).Should().BeFalse("ADR 0029: a previsão não enche a auditoria");
        typeof(IConcurrencyVersioned).IsAssignableFrom(type).Should().BeFalse("o recálculo não gera 409");
        Read("Modules", "Commercial", "BemAqui.Commercial.Infrastructure", "CommercialInfrastructure.cs").Should().NotContain($"AddAuditableEntity<{type.Name}>");
        Read("Modules", "Production", "BemAqui.Production.Infrastructure", "ProductionInfrastructure.cs").Should().NotContain($"AddAuditableEntity<{type.Name}>");
    }

    [Fact]
    public void So_o_consumidor_grava_a_previsao_no_Commercial_e_o_projeto_nao_e_tocado()
    {
        var files = Directory.GetFiles(Path.Combine(Src, "Modules"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains(".ProjectForecasts", StringComparison.Ordinal))
            .Select(Path.GetFileName).Order().ToList();
        files.Should().BeEquivalentTo(["ProjectForecastConsumer.cs", "ProjectService.Forecast.cs"]);
        Commercial("ProjectService.Forecast.cs").Should().Contain("db.ProjectForecasts.AsNoTracking()").And.NotContain("SaveChanges");

        var consumer = Commercial("ProjectForecastConsumer.cs");
        consumer.Should().Contain("db.Projects.AsNoTracking()", "CA 7: a linha de commercial.projects não é tocada (versão, 409 e auditoria intactos)");
        consumer.Should().Contain("row.ComputedAt >= integrationEvent.ComputedAt", "CA 7: o outbox não garante ordem; só o mais novo vence");
        consumer.Should().Contain("NotifyOnceAsync(", "CA 10: aviso uma vez por projeto e data");
    }

    [Fact]
    public void Previsao_do_projeto_publicada_pelo_escritor_unico()
    {
        var writer = Production("ScheduleService.cs");
        writer.Should().Contain("events.Publish(new ProjectForecastChanged(").And.Contain("plan.ScheduleProjectForecasts");
        writer.Should().Contain("push.ScheduleChangedAsync(", "CA 4: o sinal ScheduleChanged depois do commit");
        Production("ProjectForecastBuilder.cs").Should().Contain("AsNoTracking()").And.NotContain("SaveChanges");
    }

    [Fact]
    public void Funcao_unica_de_risco_nas_quatro_telas()
    {
        Production("ProductionBoardService.cs").Should().Contain("ProductionOrderRisk.Evaluate(");
        Production("FabricationOrderService.cs").Should().Contain("ProductionOrderRisk.Evaluate(");
        Read("Modules", "Production", "BemAqui.Production.Domain", "ProductionOrderRisk.cs").Should().Contain("DeliveryRiskRules.Evaluate(");
        Commercial("ProjectDelivery.cs").Should().Contain("DeliveryRiskRules.Evaluate(");
        Commercial("ProjectService.List.cs").Should().Contain("ForecastViewsAsync(");
        Commercial("ProjectService.cs").Should().Contain("ForecastViewsAsync(");
    }

    [Fact]
    public void Evento_so_com_ids_datas_e_codigo() =>
        typeof(ProjectForecastChanged).GetProperties().Select(p => Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType).Distinct()
            .Should().BeSubsetOf([typeof(Guid), typeof(DateOnly), typeof(DateTimeOffset), typeof(string)]);
}
