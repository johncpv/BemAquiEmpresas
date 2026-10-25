using BemAqui.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Purchasing.Application;

/// <summary>Dados do módulo Purchasing (ADR 0006): os handlers do módulo só enxergam estas tabelas (schema <c>purchasing</c>).</summary>
public interface IPurchasingDb
{
    DbSet<PurchaseRequest> PurchaseRequests { get; }

    DbSet<PurchaseRequestLine> PurchaseRequestLines { get; }

    DbSet<PurchasingParameter> PurchasingParameters { get; }

    DbSet<PurchaseOrder> PurchaseOrders { get; }

    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }

    DbSet<PurchaseOrderLevel> PurchaseOrderLevels { get; }

    DbSet<PurchaseOrderApproval> PurchaseOrderApprovals { get; }

    DbSet<PurchaseOrderPriceCorrection> PurchaseOrderPriceCorrections { get; }

    DbSet<GoodsReceipt> GoodsReceipts { get; }

    DbSet<GoodsReceiptLine> GoodsReceiptLines { get; }

    DbSet<Rfq> Rfqs { get; }

    DbSet<RfqSupplier> RfqSuppliers { get; }

    DbSet<RfqLine> RfqLines { get; }

    DbSet<RfqLineRequest> RfqLineRequests { get; }

    DbSet<RfqQuote> RfqQuotes { get; }

    /// <summary>Envios do pedido ao fornecedor (CMP-03b).</summary>
    DbSet<PurchaseOrderDispatch> PurchaseOrderDispatches { get; }

    /// <summary>"Enviar todos para aprovação" (CMP-02 CA 10): um envio recusado não deixa nada pendente para o envio seguinte.</summary>
    ChangeTracker ChangeTracker { get; }

    /// <summary>Transação da SC e do pedido com o número, a trava por OS da recomposição e os títulos previstos da aprovação.</summary>
    DatabaseFacade Database { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Saldo a receber dos pedidos em aberto com destino "Estoque geral", por item, para o job do mínimo da CMP-01 (CA 9). A CMP-03a liga
/// a porta ao <c>IPurchaseOrders.OpenQuantityByItemAsync</c> (<see cref="PurchaseOrdersContract"/>, CA 24).
/// </summary>
public interface IOpenPurchaseOrderQuantities
{
    Task<IReadOnlyDictionary<Guid, decimal>> GeneralStockOpenQuantityAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);
}
