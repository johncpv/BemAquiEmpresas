using BemAqui.Catalog.Contracts;
using BemAqui.Finance.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Leitura da venda pelos outros módulos (<see cref="ISaleLookup"/>) e receita por dia, loja e canal para o DRE (<see cref="ISalesRevenue"/>,
/// CA 12). Consultas agregadas no banco, sem N+1.
/// </summary>
public sealed class SalesLookupContract(ISalesDb db, SaleService sales) : ISaleLookup, ISalesRevenue
{
    public Task<SaleView?> FindAsync(Guid saleId, CancellationToken cancellationToken = default) => sales.ViewAsync(saleId, cancellationToken);

    public async Task<IReadOnlyList<SalePaymentFact>> CapturedPaymentsAsync(DateOnly businessDate, SaleChannel channel, Guid? cashSessionId, CancellationToken cancellationToken = default)
    {
        var salesOfDay = db.Sales.AsNoTracking().Where(s => s.BusinessDate == businessDate && s.Channel == channel && s.Status == SaleStatus.Completed);
        salesOfDay = cashSessionId is { } session ? salesOfDay.Where(s => s.CashSessionId == session) : salesOfDay.Where(s => s.CashSessionId == null);
        return await salesOfDay
            .Join(db.SalePayments.AsNoTracking().Where(p => p.Status == SalePaymentStatus.Captured), s => s.Id, p => p.SaleId,
                (s, p) => new SalePaymentFact(s.Id, p.Id, p.PaymentMethodId, p.AcquirerPartyId, p.CardBrand, p.Installments, p.Amount - p.ChangeAmount, s.CustomerId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionPaymentFact>> SessionPaymentsAsync(Guid cashSessionId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Sales.AsNoTracking().Where(s => s.CashSessionId == cashSessionId && s.Status == SaleStatus.Completed)
            .Join(db.SalePayments.AsNoTracking().Where(p => p.Status == SalePaymentStatus.Captured), s => s.Id, p => p.SaleId,
                (s, p) => new { s.BusinessDate, SaleId = s.Id, PaymentId = p.Id, p.PaymentMethodId, p.AcquirerPartyId, p.CardBrand, p.Installments, Amount = p.Amount - p.ChangeAmount, s.CustomerId })
            .OrderBy(x => x.BusinessDate).ThenBy(x => x.PaymentId)
            .ToListAsync(cancellationToken);
        return rows.Select(x => new SessionPaymentFact(x.BusinessDate, new SalePaymentFact(x.SaleId, x.PaymentId, x.PaymentMethodId, x.AcquirerPartyId, x.CardBrand, x.Installments, x.Amount, x.CustomerId)))
            .ToList();
    }

    public async Task<IReadOnlyList<SalesRevenueLine>> RevenueAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var rows = await db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Completed && s.BusinessDate >= from && s.BusinessDate <= to)
            .GroupBy(s => new { s.BusinessDate, s.StoreId, s.Channel })
            .Select(g => new { g.Key.BusinessDate, g.Key.StoreId, g.Key.Channel, Count = g.Count(), Gross = g.Sum(s => s.Subtotal), Discounts = g.Sum(s => s.DiscountTotal),
                Fees = g.Sum(s => s.ServiceFee), Net = g.Sum(s => s.Total) })
            .ToListAsync(cancellationToken);
        return rows.OrderBy(r => r.BusinessDate).ThenBy(r => r.Channel)
            .Select(r => new SalesRevenueLine(r.BusinessDate, r.StoreId, r.Channel, r.Count, r.Gross, r.Discounts, r.Fees, r.Net)).ToList();
    }
}

/// <summary>A forma de pagamento usada em venda não é excluída, só desativada (PAG-01 CA 6, ligado aqui).</summary>
public sealed class SalePaymentMethodUsage(ISalesDb db) : IPaymentMethodUsageProvider
{
    public async Task<string?> UsageAsync(Guid paymentMethodId, CancellationToken cancellationToken = default) =>
        await db.SalePayments.AsNoTracking().AnyAsync(p => p.PaymentMethodId == paymentMethodId, cancellationToken) ? "vendas" : null;
}

/// <summary>
/// Produto vendido não é excluído, só desativado (CAT-01, ligado aqui): conta a linha da venda pelo produto vendido ou, numa grade, pelo pai.
/// </summary>
public sealed class SaleProductUsage(ISalesDb db) : IProductUsageProvider
{
    public async Task<string?> UsageAsync(Guid productId, CancellationToken cancellationToken = default) =>
        await db.SaleLines.AsNoTracking().AnyAsync(l => l.ProductId == productId || l.ParentProductId == productId, cancellationToken) ? "vendas" : null;
}
