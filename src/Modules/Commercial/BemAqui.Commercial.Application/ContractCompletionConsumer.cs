using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// Conclusão do contrato (COM-04b CA 14 e 15): com o projeto entregue e todas as parcelas pagas ou canceladas, o contrato
/// passa a concluído e o projeto a encerrado, no último dos dois eventos; o estorno de uma baixa reabre os dois.
/// </summary>
public sealed class ContractCompletionConsumer(ICommercialDb db, IReceivables receivables, AuditOrigin origin, TimeProvider clock)
    : IIntegrationEventConsumer<ReceivableSettled>, IIntegrationEventConsumer<ProjectDelivered>
{
    public const string ReopenReason = "Baixa estornada";

    public async Task HandleAsync(ReceivableSettled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.ProjectId is not { } projectId)
        {
            return;
        }

        if (integrationEvent.IsReversal)
        {
            await ReopenAsync(projectId, integrationEvent.ReceivableId, cancellationToken);
            return;
        }

        await TryCompleteAsync(projectId, cancellationToken);
    }

    public Task HandleAsync(ProjectDelivered integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return TryCompleteAsync(integrationEvent.ProjectId, cancellationToken);
    }

    private async Task TryCompleteAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project?.Status != ProjectStatus.Delivered)
        {
            return;
        }

        var contract = await db.Contracts.FirstAsync(c => c.Id == project.ContractId, ct);
        if (contract.Status != ContractStatus.Active)
        {
            return;
        }

        // Marco recebido antes da implantação conta como quitado (COM-04c CA 5); os demais precisam do título pago ou cancelado.
        var parcels = await receivables.ForContractAsync(contract.Id, ct);
        var expected = await db.ContractMilestones.CountAsync(m => m.ContractId == contract.Id && !m.ReceivedBeforeOnboarding, ct);
        if (parcels.Count < expected || parcels.Any(p => p.Status is not (ReceivableStatus.Paid or ReceivableStatus.Cancelled)))
        {
            return;
        }

        var now = clock.GetUtcNow();
        contract.Status = ContractStatus.Completed;
        contract.CompletedAt = now;
        project.Status = ProjectStatus.Closed;
        project.ClosedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task ReopenAsync(Guid projectId, Guid receivableId, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
        {
            return;
        }

        var contract = await db.Contracts.FirstAsync(c => c.Id == project.ContractId, ct);
        if (contract.Status != ContractStatus.Completed || (await receivables.ForContractAsync(contract.Id, ct)).All(p => p.Id != receivableId))
        {
            return;
        }

        origin.Label = ReopenReason + (origin.Label is null ? string.Empty : " · " + origin.Label);
        contract.Status = ContractStatus.Active;
        contract.CompletedAt = null;
        project.Status = ProjectStatus.Delivered;
        project.ClosedAt = null;
        await db.SaveChangesAsync(ct);
    }
}
