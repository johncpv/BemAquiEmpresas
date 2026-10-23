using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Purchasing.Application;
using BemAqui.Purchasing.Contracts;
using BemAqui.Purchasing.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Purchasing.Infrastructure;

public static class PurchasingInfrastructure
{
    /// <summary>
    /// Solicitações de compra (CMP-01): serviço da tela, recomposição pela falta da BOM (consumidor, implantação e job), job do mínimo,
    /// parâmetros, o contrato <see cref="IPurchaseRequests"/> e a auditoria. O saldo a receber dos pedidos é zero até a CMP-03a.
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
        services.TryAddScoped<IOpenPurchaseOrderQuantities, NoOpenPurchaseOrders>();
        services.TryAddScoped<IPurchaseRequests, PurchaseRequestsContract>();
        services.AddAuditableEntity<PurchaseRequest>("purchasing");
        services.AddAuditableEntity<PurchaseRequestLine>("purchasing");
        services.AddAuditableEntity<PurchasingParameter>("settings");
        return services;
    }
}
