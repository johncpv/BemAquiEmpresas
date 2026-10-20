using BemAqui.Finance.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary><see cref="IFinancialOpening"/>: data de corte e conclusão da implantação, sem os lotes.</summary>
public sealed class FinancialOpeningContract(IFinanceDb db) : IFinancialOpening
{
    public async Task<FinancialOpeningInfo> StatusAsync(CancellationToken cancellationToken = default) =>
        await db.OpeningSetups.AsNoTracking().Select(s => new FinancialOpeningInfo(s.CutoverDate, s.CompletedAt)).FirstOrDefaultAsync(cancellationToken)
        ?? new FinancialOpeningInfo(null, null);
}
