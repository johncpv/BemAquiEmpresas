using System.Globalization;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

public sealed partial class EmployeeService
{
    /// <summary>Criação (CA 1 a 5): exige <c>hr.view_sensitive</c> porque CPF e salário são obrigatórios; a jornada vem do cargo.</summary>
    public async Task<Result<EmployeeCreated>> CreateAsync(EmployeeCreateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var employee = new Employee { CreatedAt = clock.GetUtcNow() };
        var position = await db.JobPositions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.PositionId && p.IsActive, cancellationToken);
        if (position is null)
        {
            return PositionService.Invalid("positionId", "Escolha um cargo ativo da lista.");
        }

        var general = new EmployeeGeneralInput(input.Name, input.Registration, input.WorkScheduleId ?? position.WorkScheduleId, null, input.AdmissionDate);
        if (await GeneralAsync(employee, general, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (await CpfAsync(employee, input.Cpf, cancellationToken) is { } badCpf)
        {
            return badCpf;
        }

        if (SalaryError(input.BaseSalary) is { } badSalary)
        {
            return badSalary;
        }

        if (input.BirthDate > BemAqui.BuildingBlocks.Time.BusinessTime.Today(clock) || input.BirthDate.Year < 1900)
        {
            return PositionService.Invalid("birthDate", "Informe a data de nascimento.");
        }

        employee.PositionId = position.Id;
        employee.BirthDate = input.BirthDate;
        employee.BaseSalary = input.BaseSalary;
        db.Employees.Add(employee);
        await db.SaveChangesAsync(cancellationToken);
        string? warning = null;
        if (position.SalaryMin is { } min && position.SalaryMax is { } max && (input.BaseSalary < min || input.BaseSalary > max))
        {
            warning = "Salário fora da faixa do cargo (" + Money(min) + "–" + Money(max) + ").";
        }

        return new EmployeeCreated((await DetailsAsync([employee], cancellationToken))[0], warning);
    }

    public async Task<Result<EmployeeDetail>> UpdateGeneralAsync(Guid id, EmployeeGeneralInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return NotFound();
        }

        if (await GeneralAsync(employee, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        await db.SaveChangesAsync(cancellationToken);
        return (await DetailsAsync([employee], cancellationToken))[0];
    }

    private async Task<Error?> GeneralAsync(Employee employee, EmployeeGeneralInput input, CancellationToken ct)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 150)
        {
            return PositionService.Invalid("name", "Informe o nome do funcionário, com até 150 caracteres.");
        }

        var registration = Trim(input.Registration, 20);
        if (registration is not null && await db.Employees.AnyAsync(e => e.Id != employee.Id && e.Registration == registration, ct))
        {
            return Error.Conflict("employee_registration_taken", "Já existe um funcionário com a matrícula " + registration + ".");
        }

        if (input.WorkScheduleId is { } sid && sid != employee.WorkScheduleId && !await db.WorkSchedules.AnyAsync(s => s.Id == sid && s.IsActive, ct))
        {
            return PositionService.Invalid("workScheduleId", "Escolha uma jornada ativa da lista.");
        }

        if (input.UnionId is { } uid && uid != employee.UnionId && !await db.LaborUnions.AnyAsync(u => u.Id == uid && u.IsActive, ct))
        {
            return PositionService.Invalid("unionId", "Escolha um sindicato ativo da lista.");
        }

        if (input.AdmissionDate.Year < 1950 || (employee.TerminationDate is { } t && input.AdmissionDate > t))
        {
            return PositionService.Invalid("admissionDate", "Informe a data de admissão, antes do desligamento.");
        }

        employee.Name = name;
        employee.Registration = registration;
        employee.WorkScheduleId = input.WorkScheduleId;
        employee.UnionId = input.UnionId;
        employee.AdmissionDate = input.AdmissionDate;
        return null;
    }

    private async Task<Error?> CpfAsync(Employee employee, string? value, CancellationToken ct)
    {
        var cpf = BrazilianDocuments.Normalize(value);
        if (!BrazilianDocuments.IsValidCpf(cpf))
        {
            return PositionService.Invalid("cpf", "CPF inválido.");
        }

        if (await db.Employees.AnyAsync(e => e.Id != employee.Id && e.Cpf == cpf, ct))
        {
            return Error.Conflict("employee_cpf_taken", "Já existe um funcionário com este CPF.");
        }

        employee.Cpf = cpf;
        return null;
    }

    private static Error? SalaryError(decimal value) =>
        value < 0 || decimal.Round(value, 2) != value || value >= 10_000_000_000m ? PositionService.Invalid("baseSalary", "Informe o salário de zero para cima, com até 2 casas.") : null;

    private static string? Digits(string? value)
    {
        var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private static string? Trim(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 ? null : text.Length > max ? text[..max] : text;
    }

    private static string Money(decimal value) => value.ToString("C", CultureInfo.GetCultureInfo("pt-BR"));
}
