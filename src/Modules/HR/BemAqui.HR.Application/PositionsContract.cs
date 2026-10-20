using BemAqui.HR.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>Cargos para os outros módulos (COM-01a): só nome e centro padrão, nada de salário.</summary>
public sealed class PositionsContract(IHrDb db) : IPositions
{
    public async Task<IReadOnlyList<PositionOption>> ActiveAsync(CancellationToken cancellationToken = default) =>
        await db.JobPositions.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name)
            .Select(p => new PositionOption(p.Id, p.Name, p.DefaultWorkCenterId, p.IsActive)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PositionOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await db.JobPositions.AsNoTracking().IgnoreQueryFilters().Where(p => ids.Contains(p.Id) && !p.IsDeleted)
            .Select(p => new PositionOption(p.Id, p.Name, p.DefaultWorkCenterId, p.IsActive)).ToListAsync(cancellationToken);
}
