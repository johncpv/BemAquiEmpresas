using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Conclusão da OS de fabricação no projeto (PRD-06a1 CA 7, 8 e 10, 04-fluxos §1 "Projeto"):
/// <list type="bullet">
/// <item>OS concluída: com todas as OS de fabricação não canceladas do projeto <c>Completed</c> (as de instalação não contam), o projeto
/// planejado ou em produção passa a <c>Fabricated</c> pelas OS e publica <see cref="ProjectFabricated"/> com a data da conclusão. Projeto
/// já fabricado (inclusive à mão) ou além não muda nem publica de novo (CA 8).</item>
/// <item>Conclusão estornada: o projeto fabricado pelas OS volta a <c>InProgress</c> e quem tem <c>finance.edit</c> é avisado das parcelas
/// "fabricação concluída" já liberadas, que continuam em aberto (o título não é estornado, ponto 83).</item>
/// </list>
/// Idempotente pela inbox e pelo estado do projeto; o registro de gatilhos do Finance não libera duas vezes.
/// </summary>
public sealed class FabricationCompletionConsumers(
    ICommercialDb db, IProductionOrders orders, ProjectService projects, IEventPublisher events, IUserDirectory users, INotificationService notifications)
    : IIntegrationEventConsumer<ProductionOrderCompleted>, IIntegrationEventConsumer<ProductionOrderCompletionReversed>
{
    public const string ReversedType = "commercial.fabrication_reversed";

    public async Task HandleAsync(ProductionOrderCompleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == integrationEvent.ProjectId, cancellationToken);
        if (project?.Status is not (ProjectStatus.Planned or ProjectStatus.InProgress))
        {
            return;
        }

        var fabrication = (await orders.ByProjectAsync(project.Id, cancellationToken))
            .Where(o => o.Type == ProductionOrderKind.Fabrication && o.Status != ProductionOrderState.Cancelled).ToList();
        if (!ProjectFabricationRules.AllCompleted(fabrication.Select(o => o.Status)))
        {
            return;
        }

        project.Status = ProjectStatus.Fabricated;
        project.FabricatedOn = integrationEvent.CompletedOn;
        project.FabricatedByOrders = true;
        events.Publish(new ProjectFabricated(project.Id, project.ContractId, integrationEvent.CompletedOn));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleAsync(ProductionOrderCompletionReversed integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == integrationEvent.ProjectId, cancellationToken);
        if (project is not { Status: ProjectStatus.Fabricated, FabricatedByOrders: true })
        {
            return;
        }

        project.Status = ProjectStatus.InProgress;
        project.FabricatedOn = null;
        project.FabricatedByOrders = false;
        var info = await ((IProjects)projects).FabricationAsync(project.Id, cancellationToken);
        var released = info?.Milestones.Where(m => m.Released && m.ReceivableId is not null).ToList() ?? [];
        if (released.Count > 0)
        {
            var number = (await orders.FindAsync(integrationEvent.ProductionOrderId, cancellationToken))?.Number ?? "OS";
            var editors = await users.UsersWithPermissionAsync("finance.edit", cancellationToken);
            foreach (var milestone in released)
            {
                foreach (var user in editors)
                {
                    notifications.Notify(user, ReversedType, ProjectFabricationRules.ReversedNotice(number, milestone.Description), "/app/financeiro/receber/" + milestone.ReceivableId);
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Regras da fabricação do projeto sem banco (PRD-06a1 CA 7 e 10).</summary>
public static class ProjectFabricationRules
{
    /// <summary>
    /// Todas as OS de fabricação não canceladas concluídas (CA 7): quem chama já tirou as de instalação e as canceladas; sem nenhuma, não.
    /// </summary>
    public static bool AllCompleted(IEnumerable<ProductionOrderState> fabricationOrders)
    {
        ArgumentNullException.ThrowIfNull(fabricationOrders);
        var list = fabricationOrders.ToList();
        return list.Count > 0 && list.All(s => s == ProductionOrderState.Completed);
    }

    /// <summary>"Conclusão da {OS} estornada. A parcela {marco} já liberada continua em aberto." (CA 10).</summary>
    public static string ReversedNotice(string order, string milestone) => $"Conclusão da {order} estornada. A parcela {milestone} já liberada continua em aberto.";
}