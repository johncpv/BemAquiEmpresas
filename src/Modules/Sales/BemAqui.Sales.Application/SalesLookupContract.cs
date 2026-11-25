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
        var salesOfDay = db.Sales.AsNoTracking().Where(s => s.BusinessDate == businessDate && s.Channel == channel && ReturnRules.Done.Contains(s.Status));
        salesOfDay = cashSessionId is { } session ? salesOfDay.Where(s => s.CashSessionId == session) : salesOfDay.Where(s => s.CashSessionId == null);
        return await salesOfDay
            .Join(db.SalePayments.AsNoTracking().Where(p => p.Status == SalePaymentStatus.Captured), s => s.Id, p => p.SaleId,
                (s, p) => new SalePaymentFact(s.Id, p.Id, p.PaymentMethodId, p.AcquirerPartyId, p.CardBrand, p.Installments, p.Amount - p.ChangeAmount, s.CustomerId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SessionPaymentFact>> SessionPaymentsAsync(Guid cashSessionId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Sales.AsNoTracking().Where(s => s.CashSessionId == cashSessionId && ReturnRules.Done.Contains(s.Status))
            .Join(db.SalePayments.AsNoTracking().Where(p => p.Status == SalePaymentStatus.Captured), s => s.Id, p => p.SaleId,
                (s, p) => new { s.BusinessDate, SaleId = s.Id, PaymentId = p.Id, p.PaymentMethodId, p.AcquirerPartyId, p.CardBrand, p.Installments, Amount = p.Amount - p.ChangeAmount, s.CustomerId })
            .OrderBy(x => x.BusinessDate).ThenBy(x => x.PaymentId)
            .ToListAsync(cancellationToken);
        // PDV-06 CA 6: o estorno no cartão das devoluções concluídas reduz o pagamento do cartão (o título da adquirente sai menor).
        var paymentIds = rows.Select(r => r.PaymentId).ToList();
        var reversed = await db.SaleReturns.AsNoTracking()
            .Where(r => r.Status == SaleReturnStatus.Completed && r.CardPaymentId != null && paymentIds.Contains(r.CardPaymentId.Value))
            .GroupBy(r => r.CardPaymentId!.Value).Select(g => new { PaymentId = g.Key, Amount = g.Sum(r => r.Total) })
            .ToDictionaryAsync(g => g.PaymentId, g => g.Amount, cancellationToken);
        return rows.Select(x => new SessionPaymentFact(x.BusinessDate, new SalePaymentFact(x.SaleId, x.PaymentId, x.PaymentMethodId, x.AcquirerPartyId, x.CardBrand, x.Installments,
                Math.Max(0m, x.Amount - reversed.GetValueOrDefault(x.PaymentId)), x.CustomerId)))
            .ToList();
    }

    public async Task<IReadOnlyList<SalesRevenueLine>> RevenueAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var rows = await db.Sales.AsNoTracking().Where(s => ReturnRules.Done.Contains(s.Status) && s.BusinessDate >= from && s.BusinessDate <= to)
            .GroupBy(s => new { s.BusinessDate, s.StoreId, s.Channel })
            .Select(g => new { g.Key.BusinessDate, g.Key.StoreId, g.Key.Channel, Count = g.Count(), Gross = g.Sum(s => s.Subtotal), Discounts = g.Sum(s => s.DiscountTotal),
                Fees = g.Sum(s => s.ServiceFee), Net = g.Sum(s => s.Total) })
            .ToListAsync(cancellationToken);
        // PDV-06 (revisão de segurança, achado 6): a devolução concluída reduz a receita no dia da devolução, pelo valor dos itens devolvidos, qualquer que
        // seja o reembolso. O vale-troca é só passivo (transferência entre contas, fora do DRE) e a venda nova paga com ele é receita normal: sem duplicar.
        var returns = await db.SaleReturns.AsNoTracking().Where(r => r.Status == SaleReturnStatus.Completed && r.BusinessDate >= from && r.BusinessDate <= to)
            .Join(db.Sales.AsNoTracking(), r => r.SaleId, s => s.Id, (r, s) => new { r.BusinessDate, s.StoreId, s.Channel, r.Total })
            .GroupBy(x => new { x.BusinessDate, x.StoreId, x.Channel })
            .Select(g => new { g.Key.BusinessDate, g.Key.StoreId, g.Key.Channel, Amount = g.Sum(x => x.Total) })
            .ToListAsync(cancellationToken);
        var lines = rows.ToDictionary(r => (r.BusinessDate, r.StoreId, r.Channel),
            r => new SalesRevenueLine(r.BusinessDate, r.StoreId, r.Channel, r.Count, r.Gross, r.Discounts, r.Fees, r.Net));
        foreach (var r in returns)
        {
            var date = r.BusinessDate!.Value;
            var line = lines.GetValueOrDefault((date, r.StoreId, r.Channel)) ?? new SalesRevenueLine(date, r.StoreId, r.Channel, 0, 0m, 0m, 0m, 0m);
            lines[(date, r.StoreId, r.Channel)] = line with { Returns = line.Returns + r.Amount, Net = line.Net - r.Amount };
        }

        return lines.Values.OrderBy(r => r.Date).ThenBy(r => r.Channel).ToList();
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
