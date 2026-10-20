using System.Globalization;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

public sealed partial class EmployeeService
{
    /// <summary>Reajuste (CA 14): salário e entrada Salary da linha do tempo no mesmo SaveChanges.</summary>
    public async Task<Result<EmployeeDetail>> ChangeSalaryAsync(Guid id, SalaryChangeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (SalaryError(input.NewSalary) is { } bad)
        {
            return bad;
        }

        if (Reason(input.EffectiveDate, input.Reason) is { } missing)
        {
            return missing;
        }

        AddSalary(employee, input.NewSalary, input.EffectiveDate!.Value, input.Reason!);
        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([employee], cancellationToken))[0];
    }

    /// <summary>Troca de cargo (CA 15): entrada Position; com NewSalary (só com hr.view_sensitive), também Salary.</summary>
    public async Task<Result<EmployeeDetail>> ChangePositionAsync(Guid id, PositionChangeInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (Reason(input.EffectiveDate, input.Reason) is { } missing)
        {
            return missing;
        }

        var position = await db.JobPositions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.PositionId && p.IsActive, cancellationToken);
        if (position is null || position.Id == employee.PositionId)
        {
            return PositionService.Invalid("positionId", "Escolha um cargo ativo, diferente do atual.");
        }

        if (input.NewSalary is { } salary)
        {
            if (!await CanSensitiveAsync(cancellationToken))
            {
                return Forbidden();
            }

            if (SalaryError(salary) is { } bad)
            {
                return bad;
            }
        }

        var old = await db.JobPositions.IgnoreQueryFilters().Where(p => p.Id == employee.PositionId).Select(p => p.Name).FirstOrDefaultAsync(cancellationToken);
        db.EmployeeHistory.Add(new EmployeeHistoryEntry
        {
            EmployeeId = employee.Id, Kind = EmployeeHistoryKind.Position, EffectiveDate = input.EffectiveDate!.Value, OldValue = old, NewValue = position.Name,
            Reason = input.Reason!.Trim(), UserId = currentUser.UserId, CreatedAt = clock.GetUtcNow(),
        });
        employee.PositionId = position.Id;
        if (input.NewSalary is { } newSalary)
        {
            AddSalary(employee, newSalary, input.EffectiveDate.Value, input.Reason);
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([employee], cancellationToken))[0];
    }

    /// <summary>Desligamento (CA 17): situação Desligado, nada apagado; o usuário vinculado não é desativado aqui.</summary>
    public async Task<Result<EmployeeDetail>> TerminateAsync(Guid id, TerminationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (input.TerminationDate < employee.AdmissionDate)
        {
            return PositionService.Invalid("terminationDate", "A data do desligamento precisa ser igual ou depois da admissão.");
        }

        employee.TerminationDate = input.TerminationDate;
        employee.Status = EmployeeStatus.Terminated;
        events.Publish(new EmployeeTerminated(employee.Id, input.TerminationDate));
        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([employee], cancellationToken))[0];
    }

    private void AddSalary(Employee employee, decimal salary, DateOnly effective, string reason)
    {
        db.EmployeeHistory.Add(new EmployeeHistoryEntry
        {
            EmployeeId = employee.Id, Kind = EmployeeHistoryKind.Salary, EffectiveDate = effective,
            OldValue = employee.BaseSalary.ToString("0.00", CultureInfo.InvariantCulture), NewValue = salary.ToString("0.00", CultureInfo.InvariantCulture),
            Reason = reason.Trim(), UserId = currentUser.UserId, CreatedAt = clock.GetUtcNow(),
        });
        employee.BaseSalary = salary;
        events.Publish(new EmployeeSalaryChanged(employee.Id, effective));
    }

    private static Error? Reason(DateOnly? effective, string? reason)
    {
        if (effective is null)
        {
            return PositionService.Invalid("effectiveDate", "Informe a data de vigência.");
        }

        return string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 200 ? PositionService.Invalid("reason", "Informe o motivo, com até 200 caracteres.") : null;
    }
}
