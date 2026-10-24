using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Finance.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Purchasing.Infrastructure;

public static class PurchasingInfrastructure
{
    /// <summary>
    /// Solicitações de compra (CMP-01): serviço da tela, recomposição pela falta da BOM (consumidor, implantação e job), job do mínimo,
    /// parâmetros, o contrato <see cref="IPurchaseRequests"/> e a auditoria. Pedidos de compra (CMP-03a): o serviço, o contrato
    /// <see cref="IPurchaseOrders"/> (que também dá o saldo a receber ao job do mínimo), o uso de fornecedor e condição, o dono de anexo
    /// <c>PurchaseOrder</c> e a auditoria dos pedidos, linhas, níveis e decisões.
    /// </summary>
    public static IServiceCollection AddPurchasingInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<PurchasingParametersService>();
        services.TryAddScoped<PurchaseRequestService>();
        services.TryAddScoped<ShortageRecomposer>();
        services.TryAddScoped<StockShortageChangedHandler>();
        services.TryAddScoped<ShortageBackfill>();
        services.TryAddScoped<MinStockPurchaseRequests>();
        services.TryAddScoped<IPurchaseRequests, PurchaseRequestsContract>();
        services.TryAddScoped<PurchaseOrderService>();
        services.TryAddScoped<PurchaseOrdersContract>();
        services.TryAddScoped<IPurchaseOrders>(sp => sp.GetRequiredService<PurchaseOrdersContract>());
        services.TryAddScoped<IOpenPurchaseOrderQuantities>(sp => sp.GetRequiredService<PurchaseOrdersContract>());
        services.TryAddScoped<PurchasingUsage>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPartyUsageProvider, PurchasingUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPaymentTermUsageProvider, PurchasingUsage>());
        services.AddAuditableEntity<PurchaseRequest>("purchasing");
        services.AddAuditableEntity<PurchaseRequestLine>("purchasing");
        services.AddAuditableEntity<PurchasingParameter>("settings");
        services.AddAuditableEntity<PurchaseOrder>("purchasing");
        services.AddAuditableEntity<PurchaseOrderLine>("purchasing");
        services.AddAuditableEntity<PurchaseOrderLevel>("purchasing");
        services.AddAuditableEntity<PurchaseOrderApproval>("purchasing");
        // Anexos do pedido (adendo à ADR 0019 §7, CMP-03a CA 21): ler com purchasing.view, gravar com purchasing.edit; pdf, png e jpg até 10 MB.
        services.AddAttachmentOwner(PurchaseOrderService.AttachmentOwner, new AttachmentOwnerRules("purchasing", ["application/pdf", "image/png", "image/jpeg"], 10 * 1024 * 1024,
            OwnerExists: async (sp, id, ct) => await sp.GetRequiredService<IPurchasingDb>().PurchaseOrders.AnyAsync(o => o.Id == id, ct)));
        return services;
    }
}

/// <summary>Uso de fornecedor e condição de pagamento pelos pedidos (CAD-01a CA 20, CAD-05b CA 17): em uso, não são excluídos, só inativados.</summary>
internal sealed class PurchasingUsage(IPurchasingDb db) : IPartyUsageProvider, IPaymentTermUsageProvider
{
    async Task<string?> IPartyUsageProvider.UsageAsync(Guid partyId, CancellationToken cancellationToken) =>
        await db.PurchaseOrders.AnyAsync(o => o.PartyId == partyId, cancellationToken) ? "pedidos de compra" : null;

    async Task<string?> IPaymentTermUsageProvider.UsageAsync(Guid termId, CancellationToken cancellationToken) =>
        await db.PurchaseOrders.AnyAsync(o => o.PaymentTermId == termId, cancellationToken) ? "pedidos de compra" : null;
}
