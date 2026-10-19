using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Identity.Contracts;

namespace BemAqui.HR.Application;

/// <summary>
/// % de encargos mudou (CAD-06 CA 16 → CAD-04a CA 6): recalcula os cargos não ajustados à mão. Roda na transação da inbox:
/// a mesma mensagem entregue duas vezes não recalcula de novo.
/// </summary>
public sealed class PayrollChargesChangedHandler(PositionService positions) : IIntegrationEventConsumer<PayrollChargesChanged>
{
    public async Task HandleAsync(PayrollChargesChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await positions.RecalculateAllAsync(integrationEvent.NewPct, cancellationToken);
    }
}
