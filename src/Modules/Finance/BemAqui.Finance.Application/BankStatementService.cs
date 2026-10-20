using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Extrato e saldos das contas (FIN-04 CA 1 a 4 e 17): saldo pela consulta da FIN-05, linhas paginadas no servidor com o
/// saldo acumulado da janela, e o documento de origem de cada transação.
/// </summary>
public sealed partial class BankStatementService(IFinanceDb db, CashFlowService balances, TimeProvider clock)
{
    public async Task<AccountBalances> BalancesAsync(CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var accounts = await db.BankAccounts.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name, Missing = a.OpeningBalance == null }).ToListAsync(cancellationToken);
        var list = new List<AccountBalance>();
        foreach (var a in accounts)
        {
            list.Add(new AccountBalance(a.Id, a.Name, await balances.BankBalanceAsync([a.Id], today, cancellationToken), a.Missing));
        }

        return new AccountBalances(list, list.Sum(a => a.CurrentBalance));
    }

    public async Task<Result<StatementResult>> StatementAsync(Guid accountId, DateOnly? from, DateOnly? to, bool withoutOrder, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var account = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return Error.NotFound("bank_account_not_found", "Conta não encontrada.");
        }

        var today = BusinessTime.Today(clock);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? start.AddMonths(1).AddDays(-1);
        if (end < start || end.DayNumber - start.DayNumber > 400)
        {
            return ReceivableService.Invalid("to", "Escolha um período de até 400 dias, com o fim depois do início.");
        }

        var startBalance = await balances.BankBalanceAsync([accountId], start.AddDays(-1), cancellationToken);
        var period = db.FinancialTransactions.AsNoTracking().Where(t => t.BankAccountId == accountId && t.Date >= start && t.Date <= end);
        var endBalance = startBalance + (await period.SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0);
        var lines = withoutOrder ? period.Where(t => t.BankEntryId != null) : period;
        var ordered = lines.OrderBy(t => t.Date).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id);
        var total = await lines.CountAsync(cancellationToken);

        // Saldo da janela: o de partida mais tudo o que veio antes desta página.
        var before = await ordered.Take(page.Skip).SumAsync(t => (decimal?)t.Amount, cancellationToken) ?? 0;
        var rows = await ordered.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        var items = await LinesAsync(accountId, rows, startBalance + before, cancellationToken);
        var startLabel = account.OpeningDate is { } opening && start <= opening.AddDays(1) ? "Saldo inicial" : "Saldo anterior";
        var hasMovement = await db.FinancialTransactions.AnyAsync(t => t.BankAccountId == accountId, cancellationToken);
        return new StatementResult(account.Id, account.Name, start, end, account.OpeningBalance is null, hasMovement, startLabel, startBalance, endBalance,
            new StatementPage(items, page.Page, page.PageSize, total));
    }
}
