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

    /// <summary>CPF/CNPJ do consumidor da venda do caixa, cifrado e com índice cego (OFF-02 CA 17).</summary>
    DbSet<SaleCustomerDocument> SaleCustomerDocuments { get; }

    DbSet<BlindIndexKey> BlindIndexKeys { get; }

    /// <summary>PDV-06: devoluções, as linhas, os vales-troca e os usos do vale.</summary>
    DbSet<SaleReturn> SaleReturns { get; }

    DbSet<SaleReturnLine> SaleReturnLines { get; }

    DbSet<StoreCredit> StoreCredits { get; }

    DbSet<StoreCreditMovement> StoreCreditMovements { get; }

    /// <summary>Para conferir se a ingestão roda na transação de quem chama (CA 3).</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Schema do módulo.</summary>
public static class SalesSchema
{
    public const string Name = "sales";
}
