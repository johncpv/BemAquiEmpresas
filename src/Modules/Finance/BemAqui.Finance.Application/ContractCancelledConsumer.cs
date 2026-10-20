using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Contrato cancelado (COM-04b CA 12): cancela as parcelas previstas e abertas sem baixa; as com baixa ficam para o
/// financeiro decidir (estornar e cancelar como origem cancelada, ou manter).
/// </summary>
public sealed class ContractCancelledConsumer(IFinanceDb db, IUserDirectory users, INotificationService notifications, TimeProvider clock)
    : IIntegrationEventConsumer<ContractCancelled>
{
    public const string NotificationType = "finance.contract_cancelled";
    public const string Reason = "Origem cancelada: contrato cancelado.";

    public async Task HandleAsync(ContractCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var receivables = await db.Receivables.Where(r => r.Origin == DocumentOrigin.Contract && r.ContractId == integrationEvent.ContractId
            && r.Status != ReceivableStatus.Paid && r.Status != ReceivableStatus.Cancelled).ToListAsync(cancellationToken);
        var editors = await users.UsersWithPermissionAsync("finance.edit", cancellationToken);
        foreach (var receivable in receivables)
        {
            if (receivable.PaidAmount == 0)
            {
                receivable.Status = ReceivableStatus.Cancelled;
                receivable.CancelReason = Reason;
                receivable.CancelledAt = clock.GetUtcNow();
                continue;
            }

            foreach (var user in editors)
            {
                notifications.Notify(user, NotificationType, $"Parcela {receivable.Number} tem baixa: estorne ou mantenha.", "/app/financeiro/receber/" + receivable.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
