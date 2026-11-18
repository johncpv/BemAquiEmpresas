using BemAqui.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary><see cref="ICashAccounts"/> (PDV-01 CA 3): as contas do tipo Caixa para os caixas do PDV.</summary>
public sealed class CashAccountsContract(IFinanceDb db) : ICashAccounts
{
    public async Task<IReadOnlyList<CashAccountOption>> ActiveAsync(CancellationToken cancellationToken = default) =>
        await db.BankAccounts.AsNoTracking().Where(a => a.Kind == BankAccountKind.Cash && a.IsActive).OrderBy(a => a.Name)
            .Select(a => new CashAccountOption(a.Id, a.Name, a.IsActive, a.SeedKey)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CashAccountOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Count == 0
            ? []
            : await db.BankAccounts.AsNoTracking().Where(a => a.Kind == BankAccountKind.Cash && ids.Contains(a.Id))
                .Select(a => new CashAccountOption(a.Id, a.Name, a.IsActive, a.SeedKey)).ToListAsync(cancellationToken);
    }
}
