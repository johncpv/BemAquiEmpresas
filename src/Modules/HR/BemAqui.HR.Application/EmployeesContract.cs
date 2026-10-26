using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// <see cref="IEmployees"/> (CA 21), só com campos não sensíveis; e o uso de cargos e sindicatos por funcionário ativo
/// (CAD-04a CA 9 e 17). <c>Status</c> é a situação de hoje (CAD-04c1 CA 3), sem tipo, motivo nem datas do afastamento.
/// </summary>
public sealed class EmployeesContract(IHrDb db, TimeProvider clock) : IEmployees, IPositionUsageProvider, IUnionUsageProvider
{
    public async Task<IReadOnlyList<EmployeeSummary>> ActiveAsync(CancellationToken cancellationToken = default) =>
        await WithStatusAsync(db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active), cancellationToken);

    public async Task<IReadOnlyList<EmployeeSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return await WithStatusAsync(db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => ids.Contains(e.Id)), cancellationToken);
    }

    public async Task<DateOnly?> BirthDateAsync(Guid employeeId, CancellationToken cancellationToken = default) =>
        await db.Employees.AsNoTracking().Where(e => e.Id == employeeId).Select(e => (DateOnly?)e.BirthDate).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BankAccountStatus>> BankAccountStatusAsync(IReadOnlyCollection<Guid> employeeIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(employeeIds);
        var now = clock.GetUtcNow();
        var rows = await db.Employees.IgnoreQueryFilters().AsNoTracking().Where(e => employeeIds.Contains(e.Id) && e.BankAccountVersion > 0)
            .Select(e => new { e.Id, e.BankAccountVersion, e.BankChangedAt, e.BankQuarantineUntil, e.BankReviewedAt, e.BankSelfReviewed }).ToListAsync(cancellationToken);
        return rows.Select(r => new BankAccountStatus(r.Id, r.BankAccountVersion == 1, r.BankChangedAt ?? DateTimeOffset.MinValue, r.BankQuarantineUntil,
            EmployeeBankRules.InQuarantine(r.BankQuarantineUntil, r.BankReviewedAt, now), r.BankReviewedAt, r.BankSelfReviewed, r.BankAccountVersion)).ToList();
    }

    public Task<bool> HasEmployeesAsync(Guid positionId, CancellationToken cancellationToken = default) =>
        db.Employees.AnyAsync(e => e.PositionId == positionId && e.Status == EmployeeStatus.Active, cancellationToken);

    public Task<bool> InUseAsync(Guid unionId, CancellationToken cancellationToken = default) =>
        db.Employees.AnyAsync(e => e.UnionId == unionId, cancellationToken);

    private async Task<List<EmployeeSummary>> WithStatusAsync(IQueryable<Employee> employees, CancellationToken ct)
    {
        var today = BusinessTime.Today(clock);
        var rows = await (
            from e in employees
            join p in db.JobPositions.IgnoreQueryFilters() on e.PositionId equals p.Id
            select new
            {
                e.Id, e.Name, e.Registration, e.PositionId, PositionName = p.Name, p.DefaultWorkCenterId, e.Status, e.IsDeleted,
                OnLeave = db.EmployeeHistory.Any(h => h.EmployeeId == e.Id && h.Kind == EmployeeHistoryKind.Leave && h.CancelledAt == null && h.EffectiveDate <= today && h.LeaveEndDate >= today),
            }).OrderBy(r => r.Name).ToListAsync(ct);
        return rows.Select(r => new EmployeeSummary(r.Id, r.Name, r.Registration, r.PositionId, r.PositionName, r.DefaultWorkCenterId,
            r.Status == EmployeeStatus.Active && !r.IsDeleted, Situation(EmployeeLeaveRules.Situation(r.Status, r.OnLeave)))).ToList();
    }

    private static EmployeeSituation Situation(EmployeeStatus status) => status switch
    {
        EmployeeStatus.Terminated => EmployeeSituation.Terminated,
        EmployeeStatus.OnLeave => EmployeeSituation.OnLeave,
        _ => EmployeeSituation.Active,
    };
}
