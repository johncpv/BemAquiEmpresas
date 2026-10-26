using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using NetArchTest.Rules;

namespace BemAqui.Architecture.Tests;

/// <summary>
/// PLN-02 (ADR 0029 §3): a fila é auditada e versionada (CA 6 e 8); só a manutenção e o "Salvar ordem" gravam nela; as rotas ficam no
/// grupo do módulo com <c>perm:</c> e a prévia com a política nomeada <c>planning-simulate</c> (CA 10 e 11); a fila nunca lê nem grava
/// <c>commercial.projects.priority</c> (CA 3); o Estoque não usa a fila para as faltas (CA 9, item 28 da Fase 3); o recálculo segue a fila.
/// </summary>
public class PlanningQueueRulesTests
{
    private static string Production => Path.Combine(Solution.RootDirectory, "src", "Modules", "Production");

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine([Production, .. parts]));

    [Fact]
    public void Fila_e_auditada_e_versionada()
    {
        typeof(IAuditable).IsAssignableFrom(typeof(PlanningQueueEntry)).Should().BeTrue("CA 6: a posição antes/depois vai para o audit_log");
        typeof(IAuditable).IsAssignableFrom(typeof(PlanningQueueState)).Should().BeTrue();
        typeof(IConcurrencyVersioned).IsAssignableFrom(typeof(PlanningQueueState)).Should().BeTrue("CA 6: a versão (xmin) dá o 409");
        var infrastructure = Read("BemAqui.Production.Infrastructure", "ProductionInfrastructure.cs");
        infrastructure.Should().Contain("AddAuditableEntity<PlanningQueueEntry>(\"production\")").And.Contain("AddAuditableEntity<PlanningQueueState>(\"production\")");
    }

    [Fact]
    public void So_a_manutencao_e_o_salvar_gravam_a_fila()
    {
        var writers = Directory.GetFiles(Path.Combine(Solution.RootDirectory, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f =>
            {
                var text = File.ReadAllText(f);
                return text.Contains("PlanningQueue.Add", StringComparison.Ordinal) || text.Contains("PlanningQueue.Remove", StringComparison.Ordinal)
                    || text.Contains("PlanningQueueStates.Add", StringComparison.Ordinal) || text.Contains("PlanningQueueChanges.Add", StringComparison.Ordinal);
            })
            .Select(Path.GetFileName).Order().ToList();
        writers.Should().BeEquivalentTo(["PlanningQueueService.cs", "PlanningQueueStore.cs"]);
    }

    [Fact]
    public void Fila_nao_usa_a_prioridade_do_projeto()
    {
        foreach (var file in new[] { "PlanningQueueStore.cs", "PlanningQueueService.cs" })
        {
            Read("BemAqui.Production.Application", file).Should().NotContain("Priority", "CA 3, ADR 0029: commercial.projects.priority fica sem uso");
        }

        Read("BemAqui.Production.Domain", "PlanningQueueRules.cs").Should().NotContain("Priority");
    }

    [Fact]
    public void Rotas_da_fila_tem_modulo_permissao_e_rate_limit()
    {
        Read("BemAqui.Production.Endpoints", "ProductionEndpoints.cs").Should().Contain("MapPlanningQueue(production.MapGroup(\"/planning/queue\"))", "RequireModule no grupo /production");
        var endpoints = Read("BemAqui.Production.Endpoints", "ProductionEndpoints.PlanningQueue.cs");
        foreach (var (name, permission) in new[] { ("GetPlanningQueue", "view"), ("ListPlanningQueueChanges", "view"), ("PreviewPlanningQueue", "edit"), ("SavePlanningQueue", "edit") })
        {
            endpoints.Split(';').Single(s => s.Contains($".WithName(\"{name}\")", StringComparison.Ordinal)).Should().Contain($"RequirePermission(\"production.{permission}\")", name);
        }

        endpoints.Split(';').Single(s => s.Contains(".WithName(\"PreviewPlanningQueue\")", StringComparison.Ordinal))
            .Should().Contain("RequireRateLimiting(PlanningQueueService.SimulatePolicy)");
        PlanningQueueService.SimulatePolicy.Should().Be("planning-simulate");
        PlanningQueueService.SimulatePerMinute.Should().Be(20);
        endpoints.Should().NotContain("RequireModule", "um RequireModule só, no grupo");
    }

    [Fact]
    public void O_recalculo_segue_a_fila_e_a_previa_nao_grava()
    {
        var writer = Read("BemAqui.Production.Application", "ScheduleService.cs");
        writer.Should().Contain("queue.SyncAsync(ct)").And.Contain("builder.BuildAsync(queueOrder, ct)", "o motor recebe a posição da fila");
        var service = Read("BemAqui.Production.Application", "PlanningQueueService.cs");
        var preview = service[service.IndexOf("PreviewAsync(", StringComparison.Ordinal)..service.IndexOf("public static bool Worsened", StringComparison.Ordinal)];
        preview.Should().NotContainAny(["SaveChanges", "SyncAsync", "RequestAsync"], "CA 4: a prévia não grava nada");
        service.Should().Contain("trigger.RequestAsync(\"fila\"", "CA 6: salvar pede o recálculo (PLN-01b)");
    }

    [Fact]
    public void Estoque_nao_usa_a_fila_para_as_faltas()
    {
        var inventory = Types.InAssemblies([typeof(BemAqui.Inventory.Application.IInventoryDb).Assembly]).That().HaveDependencyOn(typeof(IPlanningQueue).FullName).GetTypes();
        inventory.Should().BeEmpty("CA 9: item 28 da Fase 3, a ordem das faltas não é a fila");
    }
}
