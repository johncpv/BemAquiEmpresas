using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// <see cref="IEmployees"/> (CA 21), só com campos não sensíveis; e o uso de cargos e sindicatos por funcionário ativo
/// (CAD-04a CA 9 e 17).
/// </summary>
public sealed class EmployeesContract(IHrDb db) : IEmployees, IPositionUsageProvider, IUnionUsageProvider
{
    public async Task<IReadOnlyList<EmployeeSummary>> ActiveAsync(CancellationToken cancellationToken = default) =>
        await Project(db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active).OrderBy(e => e.Name)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<EmployeeSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return await Project(db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => ids.Contains(e.Id))).ToListAsync(cancellationToken);
    }

    public Task<bool> HasEmployeesAsync(Guid positionId, CancellationToken cancellationToken = default) =>
        db.Employees.AnyAsync(e => e.PositionId == positionId && e.Status == EmployeeStatus.Active, cancellationToken);

    public Task<bool> InUseAsync(Guid unionId, CancellationToken cancellationToken = default) =>
        db.Employees.AnyAsync(e => e.UnionId == unionId, cancellationToken);

    private IQueryable<EmployeeSummary> Project(IQueryable<Employee> employees) =>
        from e in employees
        join p in db.JobPositions.IgnoreQueryFilters() on e.PositionId equals p.Id
        select new EmployeeSummary(e.Id, e.Name, e.Registration, e.PositionId, p.Name, p.DefaultWorkCenterId, e.Status == EmployeeStatus.Active && !e.IsDeleted);
}
