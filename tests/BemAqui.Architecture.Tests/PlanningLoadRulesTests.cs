using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PLN-04: a ocupação só lê o plano gravado (nada é recalculado nem gravado, ADR 0029); o <see cref="IFactoryLoad"/> fica no
/// <c>Production.Contracts</c> e a tela usa a mesma conta (CA 10); as rotas ficam no grupo do módulo com <c>perm:</c> (CA 12); nenhuma
/// resposta tem custo.
/// </summary>
public class PlanningLoadRulesTests
{
    private static string Production => Path.Combine(Solution.RootDirectory, "src", "Modules", "Production");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Production, .. parts]));

    [Fact]
    public void Ocupacao_so_le_o_plano()
    {
        var service = Read("BemAqui.Production.Application", "FactoryLoadService.cs");
        service.Should().NotContainAny(["SaveChanges", "IProductionPlanDb", "ScheduleService", "RequestAsync", "FiniteScheduler.Run"], "a tela lê o plano gravado, sem recalcular");
        typeof(IFactoryLoad).Assembly.Should().BeSameAs(typeof(IProductionSchedule).Assembly, "CA 10: contrato público do Production");
        typeof(IFactoryLoad).IsAssignableFrom(typeof(FactoryLoadService)).Should().BeTrue("a tela e o contrato usam a mesma conta");
        Read("BemAqui.Production.Infrastructure", "ProductionInfrastructure.cs").Should().Contain("TryAddScoped<IFactoryLoad>(sp => sp.GetRequiredService<FactoryLoadService>())");
    }

    [Fact]
    public void Rotas_da_ocupacao_tem_modulo_e_permissao()
    {
        Read("BemAqui.Production.Endpoints", "ProductionEndpoints.cs").Should().Contain("MapPlanningLoad(production.MapGroup(\"/planning/load\"))", "RequireModule no grupo /production");
        var endpoints = Read("BemAqui.Production.Endpoints", "ProductionEndpoints.PlanningLoad.cs");
        foreach (var name in new[] { "GetFactoryLoad", "GetFactoryLoadCell" })
        {
            endpoints.Split(';').Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal)).Should().Contain("RequirePermission(\"production.view\")", name);
        }

        endpoints.Should().NotContain("RequireModule", "um RequireModule só, no grupo");
    }

    [Fact]
    public void Respostas_sem_custo()
    {
        var types = new[] { typeof(FactoryLoadWeekly), typeof(FactoryLoadRow), typeof(FactoryLoadCell), typeof(FactoryLoadWeek), typeof(FactoryLoadView), typeof(FactoryLoadCellDetail), typeof(FactoryLoadCellStep) };
        types.SelectMany(t => t.GetProperties()).Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal) || n.Contains("Amount", StringComparison.Ordinal) || n.Contains("Price", StringComparison.Ordinal));
    }
}
