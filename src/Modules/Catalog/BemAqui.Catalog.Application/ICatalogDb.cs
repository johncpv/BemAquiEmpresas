using BemAqui.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Catalog.Application;

/// <summary>Dados do módulo Catalog (ADR 0006): os handlers do módulo só enxergam estas tabelas (schema <c>catalog_products</c>).</summary>
public interface ICatalogDb
{
    DbSet<ProductCategory> ProductCategories { get; }

    DbSet<Product> Products { get; }

    DbSet<ProductBarcode> ProductBarcodes { get; }

    DbSet<PriceList> PriceLists { get; }

    DbSet<PriceListItem> PriceListItems { get; }

    /// <summary>Histórico do preço (CA 8): só inclusão.</summary>
    DbSet<PriceChange> PriceChanges { get; }

    /// <summary>Transação da criação com item novo (CA 3) e trava da tabela de preço (CA 8).</summary>
    DatabaseFacade Database { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
