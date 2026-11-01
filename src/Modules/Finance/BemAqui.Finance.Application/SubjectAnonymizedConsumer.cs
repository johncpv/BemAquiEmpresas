using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Privacy;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Titular anonimizado (LGPD-01b CA 9, ADR 0037 §4): o retrato do favorecido nas OPs (<c>finance.payment_order_items.payee_snapshot</c>) é uma
/// cópia do dado do terceiro ou do funcionário, congelada no envio. Nas OPs terminadas (paga, cancelada, recusada ou estornada), a forma de
/// pagamento fica só com o tipo; quando o cadastro teve o nome e o documento anonimizados (<see cref="SubjectAnonymized.IdentityRemoved"/>), o
/// nome vira o marcador e o documento e a matrícula saem. OP em andamento fica como está (o pagamento ainda precisa do retrato). Valores, datas e
/// ids ficam. Idempotente: rodar de novo não muda nada; a auditoria leva só o nome do campo.
/// </summary>
public sealed class SubjectAnonymizedConsumer(IFinanceDb db, AuditOrigin origin) : IIntegrationEventConsumer<SubjectAnonymized>
{
    public async Task HandleAsync(SubjectAnonymized integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        PayeeType type;
        Guid payee;
        switch (integrationEvent.SubjectType)
        {
            case PersonalDataSubjectKind.Party:
                (type, payee) = (PayeeType.Party, integrationEvent.PartyId ?? integrationEvent.SubjectId);
                break;
            case PersonalDataSubjectKind.Employee:
                (type, payee) = (PayeeType.Employee, integrationEvent.EmployeeId ?? integrationEvent.SubjectId);
                break;
            default:
                return;
        }

        var items = await (from i in db.PaymentOrderItems
                           join p in db.Payables on i.PayableId equals p.Id
                           join o in db.PaymentOrders on i.PaymentOrderId equals o.Id
                           where p.PayeeType == type && p.PayeeId == payee
                                 && (o.Status == PaymentOrderStatus.Paid || o.Status == PaymentOrderStatus.Cancelled || o.Status == PaymentOrderStatus.Rejected || o.Status == PaymentOrderStatus.Reversed)
                           select i).ToListAsync(cancellationToken);
        if (items.Count == 0)
        {
            return;
        }

        origin.AnonymizationRequest = integrationEvent.RequestNumber;
        var marker = PersonalDataMarker.Text(integrationEvent.RequestNumber);
        foreach (var item in items)
        {
            var snapshot = PaymentOrderService.Snapshot(item);
            var clean = snapshot with { Details = new PaymentDetailsInput(snapshot.Details.Kind), AccountTail = null };
            if (integrationEvent.IdentityRemoved)
            {
                clean = clean with { PartyName = marker, PartyDocument = null, Registration = null };
            }

            var json = PaymentOrderService.Serialize(clean);
            if (json != item.PayeeSnapshot)
            {
                item.PayeeSnapshot = json;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
