using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary><see cref="IReceivables"/>: só contagem por origem, sem valor nem dado do cliente.</summary>
public sealed class ReceivablesContract(IFinanceDb db) : IReceivables
{
    public Task<int> CountByOriginAsync(DocumentOrigin origin, Guid originId, CancellationToken cancellationToken = default) =>
        db.Receivables.AsNoTracking().CountAsync(r => r.Origin == origin && r.OriginId == originId, cancellationToken);
}
