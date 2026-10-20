using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>Relatório de descontos e cancelamentos do período (FIN-01 CA 22, §2.2-A5).</summary>
public sealed class ReceivableReports(IFinanceDb db, IParties parties, IFinanceParameters parameters)
{
    public async Task<DiscountsAndCancellations> DiscountsAndCancellationsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var limits = await parameters.LimitsAsync(cancellationToken);
        var discounts = await db.ReceivableSettlements.AsNoTracking()
            .Where(s => s.Discount > 0 && s.ReversedAt == null && s.Date >= from && s.Date <= to)
            .Join(db.Receivables.AsNoTracking(), s => s.ReceivableId, r => r.Id, (s, r) => new { s, r.Number, r.PartyId })
            .OrderBy(x => x.s.Date).ToListAsync(cancellationToken);
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3)).ToUniversalTime();
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(-3)).ToUniversalTime();
        var cancelled = await db.Receivables.AsNoTracking()
            .Where(r => r.Status == ReceivableStatus.Cancelled && r.CancelledAt >= start && r.CancelledAt < end)
            .OrderBy(r => r.CancelledAt).ToListAsync(cancellationToken);
        var cancelledPayables = await db.Payables.AsNoTracking()
            .Where(p => p.Status == PayableStatus.Cancelled && p.CancelledAt >= start && p.CancelledAt < end)
            .OrderBy(p => p.CancelledAt).ToListAsync(cancellationToken);
        var ids = discounts.Select(d => d.PartyId).Concat(cancelled.Select(c => c.PartyId)).Concat(cancelledPayables.Select(p => p.PartyId)).Distinct().ToList();
        var names = (await parties.FindManyAsync(ids, cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var discountLines = discounts.Select(d =>
        {
            var pct = d.s.BalanceBefore > 0 ? Math.Round(d.s.Discount / d.s.BalanceBefore * 100, 2) : 100;
            return new DiscountLine(d.s.ReceivableId, d.Number, names.GetValueOrDefault(d.PartyId, string.Empty), d.s.Date, d.s.Discount, d.s.BalanceBefore, pct, d.s.CreatedBy, d.s.CreatedAt,
                pct > limits.DiscountApprovalPct || d.s.Discount > limits.DiscountApprovalAmount);
        }).ToList();
        var cancelLines = cancelled.Select(r => new CancellationLine(r.Id, r.Number, names.GetValueOrDefault(r.PartyId, string.Empty), r.Amount - r.PaidAmount, r.CancelReason ?? string.Empty, r.CancelledBy, r.CancelledAt!.Value))
            .Concat(cancelledPayables.Select(p => new CancellationLine(p.Id, p.Number, names.GetValueOrDefault(p.PartyId, string.Empty), p.NetAmount - p.PaidAmount, p.CancelReason ?? string.Empty, p.CancelledBy, p.CancelledAt!.Value, true)))
            .OrderBy(c => c.At).ToList();
        return new DiscountsAndCancellations(from, to, discountLines, cancelLines, discountLines.Sum(d => d.Discount), cancelLines.Sum(c => c.Balance));
    }
}

/// <summary>
/// Uso de cadastros pelos títulos e transações (CA 23): com uso, a conta bancária trava o saldo inicial, e conta do plano,
/// centro de custo e terceiro deixam de ser excluídos.
/// </summary>
public sealed class FinanceUsage(IFinanceDb db) : IBankAccountUsageProvider, IAccountUsageProvider, ICostCenterUsageProvider, IPartyUsageProvider
{
    public Task<bool> HasMovementAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        db.FinancialTransactions.AnyAsync(t => t.BankAccountId == accountId, cancellationToken);

    async Task<bool> IAccountUsageProvider.HasEntriesAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.Receivables.AnyAsync(r => r.AccountId == accountId, cancellationToken) || await db.Payables.AnyAsync(p => p.AccountId == accountId, cancellationToken)
        || await db.RecurringExpenses.IgnoreQueryFilters().AnyAsync(r => r.AccountId == accountId, cancellationToken);

    async Task<bool> ICostCenterUsageProvider.HasEntriesAsync(Guid costCenterId, CancellationToken cancellationToken) =>
        await db.Receivables.AnyAsync(r => r.CostCenterId == costCenterId, cancellationToken) || await db.Payables.AnyAsync(p => p.CostCenterId == costCenterId, cancellationToken);

    public async Task<string?> UsageAsync(Guid partyId, CancellationToken cancellationToken = default) =>
        await db.Receivables.AnyAsync(r => r.PartyId == partyId, cancellationToken) ? "títulos a receber"
        : await db.Payables.AnyAsync(p => p.PartyId == partyId, cancellationToken) ? "títulos a pagar"
        : await db.RecurringExpenses.AnyAsync(r => r.PartyId == partyId, cancellationToken) ? "despesas recorrentes" : null;
}
