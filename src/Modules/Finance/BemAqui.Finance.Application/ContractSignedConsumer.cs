using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Parcelas do contrato (COM-04b CA 1 a 4, 7): um título por marco, com a conta de receita do evento. Data e assinatura
/// nascem abertos; os outros eventos nascem previstos, ou já abertos quando o evento chegou antes (registro de gatilhos).
/// </summary>
public sealed class ContractSignedConsumer(
    IFinanceDb db, INumberSequence numbers, IEnumerable<IContracts> contracts, IUserDirectory users, INotificationService notifications, TimeProvider clock)
    : IIntegrationEventConsumer<ContractSigned>
{
    public const string NotificationType = "finance.contract_receivables";

    public async Task HandleAsync(ContractSigned integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var contract = await ContractEvents.FindAsync(contracts, integrationEvent.ContractId, cancellationToken);
        if (contract?.Status == ContractState.Cancelled)
        {
            return;
        }

        var milestoneIds = integrationEvent.Milestones.Select(m => m.MilestoneId).ToList();
        var existing = await db.Receivables.Where(r => r.Origin == DocumentOrigin.Contract && r.OriginId != null && milestoneIds.Contains(r.OriginId.Value))
            .Select(r => r.OriginId!.Value).ToListAsync(cancellationToken);
        var triggers = await db.ProjectTriggerEvents.AsNoTracking().Where(t => t.ProjectId == integrationEvent.ProjectId && t.ContractId == integrationEvent.ContractId)
            .ToDictionaryAsync(t => t.Event, t => t.OccurredOn, cancellationToken);
        var number = contract?.Number ?? "Contrato";
        var created = new List<Receivable>();
        // Recebido antes da implantação (COM-04c CA 2): o dinheiro já está no saldo de corte, sem título.
        foreach (var m in integrationEvent.Milestones.OrderBy(m => m.Sequence).Where(m => !m.ReceivedBeforeOnboarding && !existing.Contains(m.MilestoneId)))
        {
            var due = m.Trigger == MilestoneTrigger.Date
                ? m.DueDate
                : m.TriggerEvent == TriggerEvent.Signature
                    ? ProjectTriggerEvent.DueDate(integrationEvent.SignedAt, m.DaysAfterEvent)
                    : m.TriggerEvent is { } trigger && triggers.TryGetValue(trigger, out var on) ? ProjectTriggerEvent.DueDate(on, m.DaysAfterEvent) : null;
            var description = $"{number} — {m.Description} ({m.Percent.ToString("0.####", ContractEvents.PtBr)}%)";
            var receivable = new Receivable
            {
                Number = await numbers.NextAsync(ReceivableService.DocType, cancellationToken), PartyId = integrationEvent.PartyId, Description = description.Length <= 200 ? description : description[..200],
                AccountId = integrationEvent.RevenueAccountId, ProjectId = integrationEvent.ProjectId, ContractId = integrationEvent.ContractId, Amount = m.Amount,
                IssueDate = integrationEvent.SignedAt, DueDate = due, Status = due is null ? ReceivableStatus.Planned : ReceivableStatus.Open, Origin = DocumentOrigin.Contract,
                OriginId = m.MilestoneId, TriggerEvent = m.Trigger == MilestoneTrigger.Event ? m.TriggerEvent : null, DaysAfterEvent = m.DaysAfterEvent, CreatedAt = clock.GetUtcNow(),
            };
            db.Receivables.Add(receivable);
            created.Add(receivable);
        }

        if (created.Count == 0)
        {
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
        var title = $"Contrato {number} gerou {created.Count} {(created.Count == 1 ? "parcela" : "parcelas")} a receber ({ReceivableService.Money(created.Sum(r => r.Amount))}).";
        foreach (var user in await users.UsersWithPermissionAsync("finance.edit", cancellationToken))
        {
            notifications.Notify(user, NotificationType, title, "/app/financeiro/receber?contrato=" + integrationEvent.ContractId);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

internal static class ContractEvents
{
    public static readonly System.Globalization.CultureInfo PtBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    public static async Task<ContractInfo?> FindAsync(IEnumerable<IContracts> sources, Guid contractId, CancellationToken ct)
    {
        foreach (var source in sources)
        {
            if (await source.FindAsync(contractId, ct) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
