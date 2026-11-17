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
