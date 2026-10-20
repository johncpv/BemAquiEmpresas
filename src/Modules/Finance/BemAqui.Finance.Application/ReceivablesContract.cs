using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary><see cref="IReceivables"/>: parcelas por origem, contrato e projeto, sem dado do cliente.</summary>
public sealed class ReceivablesContract(IFinanceDb db) : IReceivables
{
    public Task<int> CountByOriginAsync(DocumentOrigin origin, Guid originId, CancellationToken cancellationToken = default) =>
        origin == DocumentOrigin.Contract
            ? db.Receivables.AsNoTracking().CountAsync(r => r.Origin == origin && (r.ContractId == originId || r.OriginId == originId), cancellationToken)
            : db.Receivables.AsNoTracking().CountAsync(r => r.Origin == origin && r.OriginId == originId, cancellationToken);

    public async Task<IReadOnlyList<ContractReceivable>> ForContractAsync(Guid contractId, CancellationToken cancellationToken = default) =>
        await Views(db.Receivables.Where(r => r.Origin == DocumentOrigin.Contract && r.ContractId == contractId)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContractReceivable>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await Views(db.Receivables.Where(r => r.ProjectId == projectId)).ToListAsync(cancellationToken);

    private static IQueryable<ContractReceivable> Views(IQueryable<Receivable> receivables) =>
        receivables.AsNoTracking().OrderBy(r => r.DueDate == null).ThenBy(r => r.DueDate).ThenBy(r => r.Number)
            .Select(r => new ContractReceivable(r.Id, r.Number, r.OriginId ?? Guid.Empty, r.Status, r.DueDate, r.Amount, r.Amount - r.PaidAmount, r.PaidAmount > 0));
}
