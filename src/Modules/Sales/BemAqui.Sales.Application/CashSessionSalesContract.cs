using BemAqui.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Sales.Application;

/// <summary>
/// Resumo das vendas de uma sessão de caixa para o fechamento do PDV (CX-02, <see cref="ICashSessionSales"/>): consultas agregadas no banco. O
/// total por forma é o capturado líquido do troco das vendas concluídas (as canceladas saem, porque o dinheiro voltou ao cliente).
/// </summary>
public sealed class CashSessionSalesContract(ISalesDb db) : ICashSessionSales
{
    public async Task<CashSessionSalesSummary> SummaryAsync(Guid cashSessionId, IReadOnlyCollection<Guid> reconcileMethodIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reconcileMethodIds);
        var sales = db.Sales.AsNoTracking().Where(s => s.CashSessionId == cashSessionId);
        var totals = await sales.GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(s => s.Total), Discounts = g.Sum(s => s.DiscountTotal) })
            .ToListAsync(cancellationToken);
        var offlineAfter = CashSessionSalesSummary.OfflineAfter;
        var offline = await sales.CountAsync(s => s.Status == SaleStatus.Completed && s.CompletedAtDevice != null && s.CompletedAtDevice + offlineAfter < s.ReceivedAt, cancellationToken);
        var completed = sales.Where(s => s.Status == SaleStatus.Completed);
        var captured = db.SalePayments.AsNoTracking().Where(p => p.Status == SalePaymentStatus.Captured);
        var byMethod = await completed.Join(captured, s => s.Id, p => p.SaleId, (s, p) => p)
            .GroupBy(p => p.PaymentMethodId).Select(g => new CashSessionMethodTotal(g.Key, g.Sum(p => p.Amount - p.ChangeAmount)))
            .ToListAsync(cancellationToken);
        var ids = reconcileMethodIds.ToList();
        var manual = ids.Count == 0 ? [] : await completed
            .Join(captured.Where(p => p.Integration == PaymentIntegration.Manual && ids.Contains(p.PaymentMethodId)), s => s.Id, p => p.SaleId,
                (s, p) => new CashSessionManualPayment(p.Id, s.Id, s.Number, p.PaymentMethodId, p.Amount - p.ChangeAmount, p.ConfirmedAt, p.ConfirmedBy))
            .OrderBy(p => p.ConfirmedAt).ToListAsync(cancellationToken);
        var done = totals.FirstOrDefault(t => t.Status == SaleStatus.Completed);
        var cancelled = totals.FirstOrDefault(t => t.Status == SaleStatus.Cancelled);
        return new CashSessionSalesSummary(done?.Count ?? 0, cancelled?.Count ?? 0, cancelled?.Total ?? 0m, done?.Discounts ?? 0m, done?.Total ?? 0m, offline, byMethod, manual);
    }
}
