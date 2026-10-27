using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.HR.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Complemento do cadastro do funcionário para a folha (CAD-04c2): dependentes, conta para pagamento (banco e Pix) com a troca
/// protegida, adicionais, vale-transporte e documentos extras. Tudo é dado pessoal: só por rota <c>/sensitive</c> (com
/// <c>SensitiveRead</c>) e por comandos próprios que exigem <c>hr.view_sensitive</c> (ADR 0016).
/// </summary>
public sealed partial class EmployeeComplementService(
    IHrDb db,
    IPermissionChecker permissions,
    IAuditWriter audit,
    IStepUpTwoFactor stepUp,
    IUserDirectory users,
    INotificationService notifications,
    IEmailSender email,
    AttachmentService attachments,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string DependentCpfIndex = "ix_employee_dependents_cpf";
    public const string DependentNotFoundCode = "dependent_not_found";
    public const string DependentCpfTakenCode = "dependent_cpf_taken";

    /// <summary>Dependentes (CA 1 e 3), só com <c>hr.view_sensitive</c> (na rota); a leitura vai para a auditoria.</summary>
    public async Task<Result<IReadOnlyList<EmployeeDependentDetail>>> DependentsAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            return NotFound();
        }

        var list = await db.EmployeeDependents.AsNoTracking().Where(d => d.EmployeeId == employeeId).OrderBy(d => d.BirthDate).ThenBy(d => d.Name).ToListAsync(cancellationToken);
        await audit.RecordReadAsync(nameof(EmployeeDependent), employeeId.ToString(), cancellationToken);
        return list.Select(ToDetail).ToList();
    }

    public async Task<Result<EmployeeDependentDetail>> AddDependentAsync(Guid employeeId, EmployeeDependentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        if (!await db.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken))
        {
            return NotFound();
        }

        var dependent = new EmployeeDependent { EmployeeId = employeeId, CreatedAt = clock.GetUtcNow() };
        if (await ApplyDependentAsync(dependent, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.EmployeeDependents.Add(dependent);
        return await SaveDependentAsync(dependent, cancellationToken);
    }

    /// <summary>Editar (CA 1 a 3): o dependente precisa ser do funcionário da rota; de outro, 404 (IDOR).</summary>
    public async Task<Result<EmployeeDependentDetail>> UpdateDependentAsync(Guid employeeId, Guid dependentId, EmployeeDependentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var dependent = await db.EmployeeDependents.FirstOrDefaultAsync(d => d.Id == dependentId && d.EmployeeId == employeeId, cancellationToken);
        if (dependent is null)
        {
            return DependentNotFound();
        }

        if (await ApplyDependentAsync(dependent, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (input.Version is { } version)
        {
            db.Entry(dependent).Property(d => d.Version).OriginalValue = version;
        }

        return await SaveDependentAsync(dependent, cancellationToken);
    }

    /// <summary>Excluir (soft delete) o dependente do funcionário da rota; de outro, 404 (CA 3).</summary>
    public async Task<Result> DeleteDependentAsync(Guid employeeId, Guid dependentId, CancellationToken cancellationToken = default)
    {
        if (!await CanSensitiveAsync(cancellationToken))
        {
            return Forbidden();
        }

        var dependent = await db.EmployeeDependents.FirstOrDefaultAsync(d => d.Id == dependentId && d.EmployeeId == employeeId, cancellationToken);
        if (dependent is null)
        {
            return DependentNotFound();
        }

        db.EmployeeDependents.Remove(dependent);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ApplyDependentAsync(EmployeeDependent dependent, EmployeeDependentInput input, CancellationToken ct)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > DependentRules.NameMaxLength)
        {
            return DependentRules.Field("name", "Informe o nome do dependente, com até 150 caracteres.");
        }

        if (input.BirthDate is not { } birth || birth > BusinessTime.Today(clock) || birth.Year < 1900)
        {
            return DependentRules.Field("birthDate", "Informe a data de nascimento do dependente.");
        }

        if (input.Kinship is not { } kinship || !Enum.IsDefined(kinship))
        {
            return DependentRules.Field("kinship", "Escolha o parentesco.");
        }

        // Na edição, o dependente de IR que já tem CPF mantém o gravado quando o campo vem vazio (a lista só mostra o CPF mascarado).
        var keep = input.IrDependent && string.IsNullOrWhiteSpace(input.Cpf) && dependent.IrDependent && dependent.Cpf is not null;
        var (cpf, cpfError) = keep ? (dependent.Cpf, null) : DependentRules.Cpf(input.IrDependent, input.Cpf);
        if (cpfError is not null)
        {
            return cpfError;
        }

        if (cpf is not null && await db.EmployeeDependents.AnyAsync(d => d.EmployeeId == dependent.EmployeeId && d.Id != dependent.Id && d.Cpf == cpf, ct))
        {
            return CpfTaken();
        }

        dependent.Name = name;
        dependent.BirthDate = birth;
        dependent.Kinship = kinship;
        dependent.IrDependent = input.IrDependent;
        dependent.FamilyAllowance = input.FamilyAllowance;
        dependent.Cpf = cpf;
        return null;
    }

    private async Task<Result<EmployeeDependentDetail>> SaveDependentAsync(EmployeeDependent dependent, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, DependentCpfIndex))
        {
            return CpfTaken();
        }

        return ToDetail(dependent);
    }

    private static EmployeeDependentDetail ToDetail(EmployeeDependent d) =>
        new(d.Id, d.Name, d.BirthDate, d.Kinship, d.IrDependent, d.FamilyAllowance, d.Cpf is null ? null : EmployeeBankRules.MaskCpf(d.Cpf), d.Version);

    private ValueTask<bool> CanSensitiveAsync(CancellationToken ct) => permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, ct);

    /// <summary>Grava o funcionário conferindo o <c>xmin</c> enviado (ADR 0019 §2): versão antiga dá 409 <c>concurrency_conflict</c>.</summary>
    private async Task SaveEmployeeAsync(Employee employee, uint version, CancellationToken ct)
    {
        db.Entry(employee).Property(e => e.Version).OriginalValue = version;
        db.Entry(employee).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);
    }

    private static Error NotFound() => Error.NotFound("employee_not_found", "Funcionário não encontrado.");

    private static Error DependentNotFound() => Error.NotFound(DependentNotFoundCode, "Dependente não encontrado.");

    private static Error CpfTaken() => Error.Conflict(DependentCpfTakenCode, "Este CPF já está em outro dependente do funcionário.");

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos dados pessoais do funcionário. Fale com o administrador.");
}
