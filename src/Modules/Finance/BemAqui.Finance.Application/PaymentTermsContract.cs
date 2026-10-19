using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary><see cref="IPaymentTerms"/> para os outros módulos (ADR 0016).</summary>
public sealed class PaymentTermsContract(IFinanceDb db) : IPaymentTerms
{
    public async Task<PaymentTermSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var term = await db.PaymentTerms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return term is null ? null : new PaymentTermSummary(term.Id, term.Name, term.IsActive, term.Installments);
    }

    public async Task<IReadOnlyList<PaymentTermSummary>> ActiveAsync(CancellationToken cancellationToken = default) =>
        (await db.PaymentTerms.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(cancellationToken))
            .Select(t => new PaymentTermSummary(t.Id, t.Name, t.IsActive, t.Installments)).ToList();

    public async Task<IReadOnlyList<InstallmentDue>?> SplitAsync(Guid termId, decimal amount, DateOnly baseDate, CancellationToken cancellationToken = default)
    {
        var term = await FindAsync(termId, cancellationToken);
        return term is null ? null : PaymentTermSplitter.Split(term.Installments, amount, baseDate);
    }
}
