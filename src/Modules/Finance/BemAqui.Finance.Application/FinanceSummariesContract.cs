using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Resumos para o painel do proprietário (DSH-01a CA 5 e 6): chamam os mesmos serviços das telas Contas e extrato (FIN-04/05)
/// e Inadimplência (FIN-06a), então o número do card é o do relatório, centavo a centavo. Nada é recalculado aqui.
/// </summary>
public sealed class FinanceSummariesContract(IFinanceDb db, BankStatementService statements, AgingService aging, TimeProvider clock) : IBankBalances, IReceivablesAging
{
    private static readonly ReceivableStatus[] Open = [ReceivableStatus.Open, ReceivableStatus.PartiallyPaid];

    public async Task<BankBalancesSummary> CurrentAsync(CancellationToken cancellationToken = default)
    {
        var balances = await statements.BalancesAsync(cancellationToken);
        return new BankBalancesSummary(BusinessTime.Today(clock), balances.Consolidated,
            balances.Accounts.Select(a => new BankBalanceSummaryAccount(a.Id, a.Name, a.CurrentBalance, a.OpeningMissing)).ToList());
    }

    public async Task<ReceivablesAgingSummary> SummaryAsync(int top = 5, CancellationToken cancellationToken = default)
    {
        var report = (await aging.ReportAsync("-total", cancellationToken)).Value;
        var today = report.Today;
        var largest = await db.Receivables.AsNoTracking().Where(r => Open.Contains(r.Status) && r.DueDate != null && r.DueDate < today)
            .OrderByDescending(r => r.Amount - r.PaidAmount).ThenBy(r => r.DueDate).ThenBy(r => r.Number)
            .Select(r => new { r.Id, r.Number, r.PartyId, Balance = r.Amount - r.PaidAmount, Due = r.DueDate!.Value })
            .FirstOrDefaultAsync(cancellationToken);
        var customer = largest is null ? null : report.Rows.FirstOrDefault(c => c.PartyId == largest.PartyId);
        return new ReceivablesAgingSummary(
            today, report.Total, report.Count, report.Customers,
            report.Buckets.Select(b => new AgingBucketSummary(b.Label, b.FromDays, b.ToDays, b.Total, b.Count)).ToList(),
            report.Rows.Take(Math.Max(top, 0)).Select(c => new AgingCustomerSummary(c.PartyId, c.PartyName, c.Total, c.MaxDaysLate, c.AvgDelayDays)).ToList(),
            report.Rows.Count == 0 ? 0 : report.Rows.Max(c => c.MaxDaysLate),
            largest is null ? null : new LargestOverdueReceivable(largest.Id, largest.Number, largest.PartyId, customer?.PartyName ?? string.Empty, largest.Balance,
                today.DayNumber - largest.Due.DayNumber, customer?.AvgDelayDays));
    }
}
