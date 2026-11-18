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
