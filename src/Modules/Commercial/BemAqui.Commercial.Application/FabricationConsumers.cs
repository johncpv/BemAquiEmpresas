using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Commercial.Domain;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Fabricação no projeto (PRD-02 CA 8, PRD-03a CA 7, 04-fluxos §1 "Projeto"): a primeira etapa de OS iniciada passa o projeto de
/// <c>Planned</c> a <c>InProgress</c>; em qualquer outro estado, nada muda (idempotente pela inbox e pelo estado).
/// </summary>
public sealed class FabricationConsumers(ICommercialDb db) : IIntegrationEventConsumer<ProductionStepStarted>
{
    public async Task HandleAsync(ProductionStepStarted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == integrationEvent.ProjectId, cancellationToken);
        if (project?.Status == ProjectStatus.Planned)
        {
            project.Status = ProjectStatus.InProgress;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
