using BemAqui.Sales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Sales.Application;

/// <summary>Dados do módulo Sales (ADR 0006): os handlers do módulo só enxergam estas tabelas (schema <c>sales</c>).</summary>
public interface ISalesDb
{
    DbSet<Sale> Sales { get; }

    DbSet<SaleLine> SaleLines { get; }

    DbSet<SalePart> SaleParts { get; }

    DbSet<SalePayment> SalePayments { get; }

    DbSet<SaleStockPosting> SaleStockPostings { get; }

    /// <summary>Para conferir se a ingestão roda na transação de quem chama (CA 3).</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Schema do módulo.</summary>
public static class SalesSchema
{
    public const string Name = "sales";
}

/// <summary>
/// Produto vendável como a venda precisa dele: <b>cópia, campo a campo, do <c>SellableProduct</c> de <c>Catalog.Contracts</c></b> (CAT-01, commit
/// 11468cae), para a troca do adaptador ser direta. <c>ProductId</c> numa grade é a variação; <c>ParentProductId</c>, o pai; <c>ItemId</c>, o item
/// de estoque por onde a venda baixa; <c>Price</c>, o preço vigente da tabela padrão (null = sem preço); <c>IsActive</c> falso para desativado ou
/// excluído (a resolução por id devolve mesmo assim).
/// </summary>
public sealed record SellableProductInfo(
    Guid ProductId,
    Guid? ParentProductId,
    string Description,
    Guid UnitId,
    string UnitCode,
    Guid ItemId,
    decimal? Price,
    Guid PriceListId,
    long PriceListVersion,
    bool IsActive,
    bool IsWeighable,
    string? Plu);

/// <summary>
/// Porta interna do Sales para ler o produto vendável (VND-01), com a mesma forma do <c>ISellableProducts</c> da CAT-01. <b>Único ponto de
/// acoplamento com o catálogo:</b> até a CAT-01 entrar na <c>main</c>, o adaptador <c>InventoryItemProductResolver</c> (Infrastructure) resolve
/// o produto pelo item de estoque de mesmo id; depois, o adaptador chama o <c>ISellableProducts</c> e nada mais muda.
/// </summary>
public interface ISaleProductResolver
{
    /// <summary>Lote de ids numa consulta só; os que não existem ficam fora da lista (como o <c>ISellableProducts.FindManyAsync</c>).</summary>
    Task<IReadOnlyList<SellableProductInfo>> FindManyAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken = default);
}
