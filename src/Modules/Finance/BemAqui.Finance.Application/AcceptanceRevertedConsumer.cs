using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Aceite do cliente desfeito (EXE-01a CA 18 e 19): com os títulos travados (<c>FOR UPDATE</c>, dentro da transação do consumidor), volta a
/// <c>Planned</c> (vencimento nulo) os títulos de gatilho "aceite" do mesmo contrato e projeto que estão <c>Open</c>, sem baixa, sem boleto e não
/// cancelados, e marca o gatilho da versão como desfeito. Se algum título não pode voltar (baixa, boleto ou mudança depois da checagem do
/// Commercial), nenhum volta: o gatilho fica com <c>reverted = false</c> e o motivo, e quem tem <c>finance.edit</c> decide (compensação manual,
/// como o estorno da OS). Versão maior já gravada: evento velho, ignorado.
/// </summary>
public sealed class AcceptanceRevertedConsumer(
    IFinanceDb db, IEnumerable<IContracts> contracts, IEnumerable<IProjects> projects, IUserDirectory users, INotificationService notifications, TimeProvider clock)
    : IIntegrationEventConsumer<TechnicalAcceptanceReverted>
{
    public const string NotificationType = "finance.acceptance_reverted";
    public const string BlockedNote = "Título já em cobrança (baixa ou boleto) quando o aceite foi desfeito; decida: cancelar, manter ou fazer um aditivo.";

    public async Task HandleAsync(TechnicalAcceptanceReverted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var contract = await ContractEvents.FindAsync(contracts, integrationEvent.ContractId, cancellationToken);
        if (contract is null || contract.ProjectId != integrationEvent.ProjectId)
        {
            return;
        }

        var trigger = await db.ProjectTriggerEvents.FirstOrDefaultAsync(t => t.ProjectId == integrationEvent.ProjectId && t.Event == TriggerEvent.TechnicalAcceptance, cancellationToken);
        if (trigger is not null && (trigger.Version > integrationEvent.Version || (trigger.Version == integrationEvent.Version && trigger.Reverted)))
        {
            return;
        }

        if (trigger is null)
        {
            // Desfazer chegou antes do aceite (fora de ordem): grava o gatilho já desfeito, e o aceite da mesma versão será descartado.
            trigger = new ProjectTriggerEvent
            {
                ProjectId = integrationEvent.ProjectId, ContractId = integrationEvent.ContractId, Event = TriggerEvent.TechnicalAcceptance, OccurredOn = integrationEvent.OccurredOn,
                ReceivedAt = clock.GetUtcNow(), Version = integrationEvent.Version, Reverted = true,
            };
            db.ProjectTriggerEvents.Add(trigger);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var contractId = integrationEvent.ContractId;
        var projectId = integrationEvent.ProjectId;
        var titles = await db.Receivables
            .FromSql($"SELECT r.*, r.xmin FROM finance.receivables r WHERE r.origin = 'Contract' AND r.contract_id = {contractId} AND r.project_id = {projectId} AND r.trigger_event = 'TechnicalAcceptance' AND r.status <> 'Cancelled' AND r.status <> 'Planned' FOR UPDATE")
            .ToListAsync(cancellationToken);
        var ids = titles.Select(t => t.Id).ToList();
        var settled = ids.Count == 0 ? [] : await db.ReceivableSettlements.AsNoTracking().Where(s => ids.Contains(s.ReceivableId) && s.ReversedAt == null).Select(s => s.ReceivableId).Distinct().ToListAsync(cancellationToken);
        var blocked = titles.Where(t => !CanReturn(t) || settled.Contains(t.Id)).ToList();
        trigger.Version = integrationEvent.Version;
        if (blocked.Count > 0)
        {
            trigger.Reverted = false;
            trigger.RevertNote = BlockedNote;
            await db.SaveChangesAsync(cancellationToken);
            var editors = await users.UsersWithPermissionAsync("finance.edit", cancellationToken);
            var project = projects.FirstOrDefault() is { } reader ? await reader.FindAsync(projectId, cancellationToken) : null;
            var number = project?.Number ?? contract.Number;
            foreach (var title in blocked)
            {
                foreach (var user in editors)
                {
                    notifications.Notify(user, NotificationType,
                        $"O aceite do projeto {number} foi desfeito, mas a parcela {title.Number} já está em cobrança. Decida: cancelar, manter ou fazer um aditivo.",
                        "/app/financeiro/receber/" + title.Id);
                }
            }

            return;
        }

        foreach (var title in titles)
        {
            title.Status = ReceivableStatus.Planned;
            title.DueDate = null;
        }

        trigger.Reverted = true;
        trigger.RevertNote = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Só volta a <c>Planned</c> o título <c>Open</c>, sem valor pago e sem boleto em andamento.</summary>
    public static bool CanReturn(Receivable title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return title.Status == ReceivableStatus.Open && title.PaidAmount == 0 && title.BillingSlipId is null;
    }
}
