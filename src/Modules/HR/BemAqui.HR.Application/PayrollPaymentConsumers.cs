using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Finance.Contracts;

namespace BemAqui.HR.Application;

/// <summary>
/// OP paga ou estornada (RH-02c CA 12): a folha reconcilia pela fonte (<c>IPayables.StatusByOriginAsync</c>), sem confiar no evento. Roda na
/// transação da inbox; a mesma mensagem entregue duas vezes não muda nada (a reconciliação é idempotente).
/// </summary>
public sealed class PayrollPaymentConsumers(PayrollClosingService closing) : IIntegrationEventConsumer<PaymentOrderPaid>, IIntegrationEventConsumer<PaymentOrderReversed>
{
    public Task HandleAsync(PaymentOrderPaid integrationEvent, CancellationToken cancellationToken) => closing.ReconcileAsync(cancellationToken);

    public Task HandleAsync(PaymentOrderReversed integrationEvent, CancellationToken cancellationToken) => closing.ReconcileAsync(cancellationToken);
}
