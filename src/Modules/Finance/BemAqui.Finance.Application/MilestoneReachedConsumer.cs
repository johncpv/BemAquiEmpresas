using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Marco do projeto atingido (COM-04b CA 6 a 8, 17): grava o gatilho (o primeiro vale) e libera as parcelas previstas do
/// mesmo contrato e projeto com esse evento. Contrato cancelado não libera nada; parcela já aberta não muda.
/// </summary>
public sealed class MilestoneReachedConsumer(IFinanceDb db, IEnumerable<IContracts> contracts, IUserDirectory users, INotificationService notifications, TimeProvider clock)
    : IIntegrationEventConsumer<ProjectFabricated>, IIntegrationEventConsumer<ProjectDelivered>, IIntegrationEventConsumer<TechnicalAcceptance>
{
    public const string NotificationType = "finance.milestone_released";

    public Task HandleAsync(ProjectFabricated integrationEvent, CancellationToken cancellationToken) => ReachedAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(ProjectDelivered integrationEvent, CancellationToken cancellationToken) => ReachedAsync(integrationEvent, cancellationToken);

    public Task HandleAsync(TechnicalAcceptance integrationEvent, CancellationToken cancellationToken) => ReachedAsync(integrationEvent, cancellationToken);

    private async Task ReachedAsync(IProjectMilestoneReached reached, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(reached);
        var contract = await ContractEvents.FindAsync(contracts, reached.ContractId, ct);
        if (contract is null || contract.Status == ContractState.Cancelled || contract.ProjectId != reached.ProjectId)
        {
            return;
        }

        var trigger = await db.ProjectTriggerEvents.FirstOrDefaultAsync(t => t.ProjectId == reached.ProjectId && t.Event == reached.Event, ct);
        if (trigger is null)
        {
            trigger = new ProjectTriggerEvent { ProjectId = reached.ProjectId, ContractId = reached.ContractId, Event = reached.Event, OccurredOn = reached.OccurredOn, ReceivedAt = clock.GetUtcNow() };
            db.ProjectTriggerEvents.Add(trigger);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, ReceivableService.TriggerEventIndex))
            {
                // Outro consumidor gravou o mesmo gatilho: a reentrega libera com ele.
                throw new InvalidOperationException("Gatilho gravado em paralelo; a mensagem será reprocessada.", ex);
            }
        }

        var planned = await db.Receivables.Where(r => r.Origin == DocumentOrigin.Contract && r.ContractId == reached.ContractId && r.ProjectId == reached.ProjectId
            && r.Status == ReceivableStatus.Planned && r.TriggerEvent == reached.Event).ToListAsync(ct);
        if (planned.Count == 0)
        {
            return;
        }

        var editors = await users.UsersWithPermissionAsync("finance.edit", ct);
        foreach (var receivable in planned)
        {
            receivable.Status = ReceivableStatus.Open;
            receivable.DueDate = ProjectTriggerEvent.DueDate(trigger.OccurredOn, receivable.DaysAfterEvent);
            foreach (var user in editors)
            {
                notifications.Notify(user, NotificationType, $"Parcela {receivable.Number} liberada: {Milestone(receivable.Description)}.", "/app/financeiro/receber/" + receivable.Id);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>"CTR-2026-0001 — Fabricação concluída (40%)" → "Fabricação concluída (40%)".</summary>
    private static string Milestone(string description)
    {
        var dash = description.IndexOf(" — ", StringComparison.Ordinal);
        return dash < 0 ? description : description[(dash + 3)..];
    }
}
