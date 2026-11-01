using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Fiscal.Domain;
using BemAqui.Production.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices.Nfse;

/// <summary>
/// Aceite técnico da OS de instalação (COM-06 → FIS-06 CA 1): a OS entra na fila "A emitir" da NFS-e (<c>fiscal.installation_billings</c>).
/// Idempotente pela inbox, pela conferência e pelo índice único da OS (a segunda entrega concorrente para nele e volta pela inbox). Só ids e a
/// data; os itens, o cliente e os valores vêm na hora pelos contratos dos donos.
/// </summary>
public sealed class InstallationBillingConsumer(IFiscalDb db, TimeProvider clock) : IIntegrationEventConsumer<InstallationAccepted>
{
    public async Task HandleAsync(InstallationAccepted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (await db.InstallationBillings.AnyAsync(b => b.InstallationOrderId == integrationEvent.ProductionOrderId, cancellationToken))
        {
            return;
        }

        db.InstallationBillings.Add(new InstallationBilling
        {
            InstallationOrderId = integrationEvent.ProductionOrderId, ProjectId = integrationEvent.ProjectId, AcceptedOn = integrationEvent.AcceptedOn, CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
