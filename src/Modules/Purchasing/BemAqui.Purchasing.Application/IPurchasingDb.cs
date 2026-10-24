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

    /// <summary>Transação da SC com o número e a trava por OS da recomposição.</summary>
    DatabaseFacade Database { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Saldo a receber dos pedidos em aberto com destino "Estoque geral", por item (CMP-03a CA 24, <c>IPurchaseOrders.OpenQuantityByItemAsync</c>).
/// Até o pedido existir, a parcela é zero (<see cref="NoOpenPurchaseOrders"/>); a CMP-03a troca a implementação.
/// </summary>
public interface IOpenPurchaseOrderQuantities
{
    Task<IReadOnlyDictionary<Guid, decimal>> GeneralStockOpenQuantityAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default);
}

/// <summary>Sem pedido de compra ainda (CMP-03a): nada a receber.</summary>
public sealed class NoOpenPurchaseOrders : IOpenPurchaseOrderQuantities
{
    public Task<IReadOnlyDictionary<Guid, decimal>> GeneralStockOpenQuantityAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
}
