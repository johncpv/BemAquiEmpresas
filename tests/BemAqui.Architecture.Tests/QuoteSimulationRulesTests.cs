using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PLN-05: a simulação de prazo do orçamento fica no grupo do módulo <c>quotes</c> com <c>quotes.view</c> e a política nomeada
/// <c>planning-simulate</c> (CA 10 e 11); o Commercial chama só o contrato do Production (ADR 0006); nem o serviço do orçamento nem a
/// simulação do motor gravam, pedem recálculo ou pegam o lock da fila ou do plano (CA 5).
/// </summary>
public class QuoteSimulationRulesTests
{
    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Solution.RootDirectory, "src", "Modules", .. parts]));

    [Fact]
    public void Rota_tem_modulo_permissao_e_rate_limit()
    {
        Read("Commercial", "BemAqui.Commercial.Endpoints", "CommercialEndpoints.cs").Should().Contain("MapScheduleSimulation(quotes)", "RequireModule(\"quotes\") no grupo");
        var endpoint = Read("Commercial", "BemAqui.Commercial.Endpoints", "CommercialEndpoints.ScheduleSimulation.cs");
        endpoint.Should().Contain("RequirePermission(\"quotes.view\")").And.Contain("RequireRateLimiting(PlanningRateLimits.SimulatePolicy)").And.Contain("WithName(\"SimulateQuoteSchedule\")");
        endpoint.Should().NotContain("RequireModule", "um RequireModule só, no grupo");
        PlanningRateLimits.SimulatePolicy.Should().Be(PlanningQueueService.SimulatePolicy).And.Be("planning-simulate");
        PlanningRateLimits.SimulatePerMinute.Should().Be(20);
    }

    [Fact]
    public void Simulacao_nao_grava_nem_pega_lock()
    {
        var service = Read("Commercial", "BemAqui.Commercial.Application", "QuoteScheduleSimulationService.cs");
        service.Should().NotContainAny(["SaveChanges", "ExecuteUpdate", "ExecuteDelete", "ExecuteSql", "BeginTransaction", ".Attach(", ".Update("], "CA 5: nada é gravado");
        System.Text.RegularExpressions.Regex.IsMatch(service, @"db\.\w+\.(Add|AddRange|Remove|RemoveRange)\(").Should().BeFalse("nenhuma tabela recebe linha");
        service.Should().Contain("IProductionSchedule").And.NotContain("BemAqui.Production.Application", "o Commercial usa só o contrato");
        var contract = Read("Production", "BemAqui.Production.Application", "ProductionScheduleContract.cs");
        var simulate = contract[contract.IndexOf("public async Task<DeliverySimulation> SimulateDeliveryAsync", StringComparison.Ordinal)..contract.IndexOf("public async Task<IReadOnlyList<ProjectDeliveryForecast>> ProjectForecastsAsync", StringComparison.Ordinal)];
        simulate.Should().Contain("queue.OrderAsync").And.NotContainAny(["SaveChanges", "SyncAsync", "LockAsync", "RequestAsync", "pg_advisory"], "só lê a fila, sem o lock");
    }
}
