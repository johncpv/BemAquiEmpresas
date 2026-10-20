using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// OS de instalação no projeto (COM-06 CA 6, 10): iniciada, o projeto passa a Instalando; com todas as OS não canceladas
/// aceitas, passa a Entregue e publica o aceite técnico e a entrega com a data do último aceite.
/// </summary>
public sealed class InstallationConsumers(ICommercialDb db, IInstallationOrders installations, IEventPublisher events)
    : IIntegrationEventConsumer<InstallationStarted>, IIntegrationEventConsumer<InstallationAccepted>
{
    public async Task HandleAsync(InstallationStarted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == integrationEvent.ProjectId, cancellationToken);
        if (project?.Status is ProjectStatus.Planned or ProjectStatus.InProgress or ProjectStatus.Fabricated)
        {
            project.Status = ProjectStatus.Installing;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task HandleAsync(InstallationAccepted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == integrationEvent.ProjectId, cancellationToken);
        if (project is null || project.Status is ProjectStatus.Delivered or ProjectStatus.Closed or ProjectStatus.Cancelled)
        {
            return;
        }

        var orders = (await installations.ByProjectAsync(project.Id, cancellationToken)).Where(o => o.Status != InstallationState.Cancelled).ToList();
        if (orders.Count == 0 || orders.Any(o => o.Status != InstallationState.Completed))
        {
            return;
        }

        project.Status = ProjectStatus.Delivered;
        project.DeliveredOn = integrationEvent.AcceptedOn;
        project.DeliveredManually = false;
        events.Publish(new TechnicalAcceptance(project.Id, project.ContractId, integrationEvent.AcceptedOn));
        events.Publish(new ProjectDelivered(project.Id, project.ContractId, integrationEvent.AcceptedOn));
        await db.SaveChangesAsync(cancellationToken);
    }
}
