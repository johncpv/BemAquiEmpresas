using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Commercial.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Aditivo aprovado (COM-07 CA 4, 7): cria as parcelas dos marcos do acréscimo (como o contrato, já abertas se o gatilho
/// chegou) ou reduz os títulos indicados, revalidando cada um; título que ganhou baixa não muda e o financeiro decide.
/// </summary>
public sealed class ContractAmendedConsumer(IFinanceDb db, ContractSignedConsumer parcels, BillingSlipReactions billing, IUserDirectory users, INotificationService notifications, TimeProvider clock)
    : IIntegrationEventConsumer<ContractAmended>
{
    public const string NotificationType = "finance.contract_amended";

    public async Task HandleAsync(ContractAmended integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (integrationEvent.NewMilestones.Count > 0)
        {
            await parcels.CreateParcelsAsync(integrationEvent.ContractId, integrationEvent.ProjectId, integrationEvent.PartyId, integrationEvent.RevenueAccountId,
                integrationEvent.ApprovedOn, integrationEvent.NewMilestones, cancellationToken);
        }

        if (integrationEvent.Reductions.Count == 0)
        {
            return;
        }

        var ids = integrationEvent.Reductions.Select(r => r.ReceivableId).ToList();
        var targets = await db.Receivables.Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var skipped = new List<string>();
        foreach (var reduction in integrationEvent.Reductions)
        {
            if (!targets.TryGetValue(reduction.ReceivableId, out var receivable) || receivable.ContractId != integrationEvent.ContractId || receivable.Origin != DocumentOrigin.Contract)
            {
                skipped.Add("título fora do contrato");
                continue;
            }

            if (receivable.PaidAmount > 0 || receivable.Status is not (ReceivableStatus.Planned or ReceivableStatus.Open) || reduction.NewAmount < 0 || reduction.NewAmount > receivable.Amount)
            {
                skipped.Add(receivable.Number);
                continue;
            }

            // BNK-01b CA 17: o boleto do valor antigo é cancelado no Inter.
            await billing.RequestCancelAsync(receivable, reduction.NewAmount == 0 ? Domain.BillingSlipCancelReason.ReceivableCancelled : Domain.BillingSlipCancelReason.AmountChanged, cancellationToken);
            if (reduction.NewAmount == 0)
            {
                receivable.Status = ReceivableStatus.Cancelled;
                receivable.CancelReason = $"Reduzido a zero pelo aditivo {integrationEvent.AmendmentNumber}.";
                receivable.CancelledAt = clock.GetUtcNow();
            }
            else
            {
                receivable.Amount = reduction.NewAmount;
            }
        }

        foreach (var user in skipped.Count == 0 ? [] : await users.UsersWithPermissionAsync("finance.edit", cancellationToken))
        {
            notifications.Notify(user, NotificationType, $"Aditivo {integrationEvent.AmendmentNumber}: não reduzi {string.Join(", ", skipped)} (com baixa ou inválido). Decida no título.",
                "/app/financeiro/receber?contrato=" + integrationEvent.ContractId);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
