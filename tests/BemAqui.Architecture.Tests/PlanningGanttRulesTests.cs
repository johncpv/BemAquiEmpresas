using BemAqui.Production.Application;
using FluentAssertions;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PLN-03: o cronograma só lê o plano gravado e a fila (nada é recalculado nem gravado, ADR 0029; "só leitura"); as rotas ficam no grupo do
/// módulo com <c>perm:</c>, e a do projeto pede também <c>projects.view</c> (CA 11 e 14); nenhuma resposta tem custo nem dado pessoal; o
/// <c>Gantt</c> do front é próprio, em SVG, sem biblioteca nova (ADR 0033).
/// </summary>
public class PlanningGanttRulesTests
{
    private static string Production => Path.Combine(Solution.RootDirectory, "src", "Modules", "Production");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Production, .. parts]));

    [Fact]
    public void Cronograma_so_le_o_plano()
    {
        var service = Read("BemAqui.Production.Application", "GanttService.cs");
        service.Should().NotContainAny(["SaveChanges", "IProductionPlanDb", "ScheduleService", "RequestAsync", "FiniteScheduler.Run", "SyncAsync", "ExecuteUpdate", "ExecuteDelete", "BeginTransaction"],
            "o cronograma não muda datas nem a fila");
    }

    [Fact]
    public void Rotas_do_cronograma_tem_modulo_e_permissao()
    {
        Read("BemAqui.Production.Endpoints", "ProductionEndpoints.cs").Should().Contain("MapPlanningGantt(production.MapGroup(\"/planning\"))", "RequireModule no grupo /production");
        var endpoints = Read("BemAqui.Production.Endpoints", "ProductionEndpoints.PlanningGantt.cs");
        foreach (var name in new[] { "GetProjectsGantt", "GetCentersGantt", "GetProjectGantt" })
        {
            endpoints.Split(';').Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal)).Should().Contain("RequirePermission(\"production.view\")", name);
        }

        endpoints.Split(';').Single(s => s.Contains(".WithName(\"GetProjectGantt\")", StringComparison.Ordinal)).Should().Contain("RequirePermission(\"projects.view\")", "CA 11");
        endpoints.Should().NotContain("RequireModule", "um RequireModule só, no grupo");
        endpoints.Should().NotContainAny(["MapPost", "MapPut", "MapDelete", "MapPatch"], "só leitura");
    }

    [Fact]
    public void Respostas_sem_custo_nem_dado_pessoal()
    {
        var types = new[]
        {
            typeof(GanttProjectsView), typeof(GanttProject), typeof(GanttOrder), typeof(GanttStep), typeof(GanttOption), typeof(GanttCentersView), typeof(GanttCenterRow), typeof(GanttCenterBar),
            typeof(GanttCenterDay),
        };
        types.SelectMany(t => t.GetProperties()).Select(p => p.Name)
            .Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal) || n.Contains("Amount", StringComparison.Ordinal) || n.Contains("Price", StringComparison.Ordinal)
                || n.Contains("Value", StringComparison.Ordinal) || n.Contains("Installer", StringComparison.Ordinal) || n.Contains("Document", StringComparison.Ordinal));
    }

    [Fact]
    public void Gantt_do_front_e_proprio_em_SVG_sem_biblioteca()
    {
        var web = Path.Combine(Solution.RootDirectory, "src", "Web");
        var gantt = File.ReadAllText(Path.Combine(web, "src", "components", "ui", "gantt.tsx"));
        gantt.Should().Contain("<svg").And.Contain("role=\"img\"").And.Contain("aria-describedby");
        File.ReadAllText(Path.Combine(web, "package.json")).Should().NotContainAny(["gantt", "Gantt", "frappe", "dhtmlx", "bryntum", "vis-timeline"], "ADR 0033: sem biblioteca de Gantt");
    }
}
