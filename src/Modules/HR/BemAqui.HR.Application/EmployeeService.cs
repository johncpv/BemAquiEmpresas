using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Funcionários (CAD-04b). Comandos separados (ADR 0016): criação, dados gerais, dados pessoais, reajuste, troca de cargo,
/// desligamento e vínculo com o usuário. A lista e o detalhe nunca trazem dado sensível; a busca por CPF é só exata e só com
/// <c>hr.view_sensitive</c> (sem oráculo, CA 11).
/// </summary>
public sealed partial class EmployeeService(
    IHrDb db,
    IWorkCenters workCenters,
    IPermissionChecker permissions,
    IAuditWriter audit,
    IEventPublisher events,
    IUserEmployeeLink links,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string CpfIndex = "ix_employees_cpf";
    public const string RegistrationIndex = "ix_employees_registration";

    public async Task<PagedList<EmployeeDetail>> ListAsync(EmployeeListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var employees = db.Employees.AsNoTracking();
        if (query.Status is { } status)
        {
            employees = employees.Where(e => e.Status == status);
        }

        if (query.PositionId is { } position)
        {
            employees = employees.Where(e => e.PositionId == position);
        }

        if (query.WorkCenterId is { } center)
        {
            var positions = db.JobPositions.IgnoreQueryFilters().Where(p => p.DefaultWorkCenterId == center).Select(p => p.Id);
            employees = employees.Where(e => positions.Contains(e.PositionId));
        }

        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            var like = "%" + term + "%";
            var cpf = BrazilianDocuments.Normalize(term);
            var exactCpf = cpf.Length == 11 && BrazilianDocuments.IsValidCpf(cpf) && await CanSensitiveAsync(cancellationToken) ? cpf : null;
            employees = employees.Where(e => EF.Functions.ILike(e.Name, like) || (e.Registration != null && EF.Functions.ILike(e.Registration, like)) || (exactCpf != null && e.Cpf == exactCpf));
        }

        // Ordem só por campos não sensíveis; salário e nascimento são ignorados (CA 11).
        employees = page.ParseSort() switch
        {
            ("admissionDate", true) => employees.OrderByDescending(e => e.AdmissionDate),
            ("admissionDate", false) => employees.OrderBy(e => e.AdmissionDate),
            ("name", true) => employees.OrderByDescending(e => e.Name),
            _ => employees.OrderBy(e => e.Name),
        };
        var paged = await employees.ToPagedListAsync(page, cancellationToken);
        return new PagedList<EmployeeDetail>(await DetailsAsync(paged.Items, cancellationToken), paged.Page, paged.PageSize, paged.Total);
    }

    public async Task<Result<EmployeeDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        return employee is null ? NotFound() : (await DetailsAsync([employee], cancellationToken))[0];
    }

    /// <summary>Dados pessoais e salário (CA 9): a rota exige <c>hr.view_sensitive</c>; a leitura vai para a auditoria.</summary>
    public async Task<Result<EmployeeSensitiveDetail>> GetSensitiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (e is null)
        {
            return NotFound();
        }

        await audit.RecordReadAsync(nameof(Employee), id.ToString(), cancellationToken);
        return new EmployeeSensitiveDetail(e.Id, e.Cpf, e.BirthDate, e.BaseSalary, e.Rg, e.Pis, e.CtpsNumber, e.CtpsSeries, e.VoterId,
            e.ZipCode, e.Street, e.Number, e.Complement, e.District, e.City, e.State, e.Phone, e.Email);
    }

    /// <summary>Linha do tempo (CA 16): trocas de cargo com <c>hr.view</c>; reajustes só com <c>hr.view_sensitive</c>.</summary>
    public async Task<Result<IReadOnlyList<TimelineEntry>>> TimelineAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Employees.AnyAsync(e => e.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var sensitive = await CanSensitiveAsync(cancellationToken);
        return await db.EmployeeHistory.AsNoTracking().Where(h => h.EmployeeId == id && (sensitive || h.Kind != EmployeeHistoryKind.Salary))
            .OrderByDescending(h => h.EffectiveDate).ThenByDescending(h => h.CreatedAt)
            .Select(h => new TimelineEntry(h.Id, h.Kind, h.EffectiveDate, h.OldValue, h.NewValue, h.Reason, h.UserId, h.CreatedAt)).ToListAsync(cancellationToken);
    }

    private ValueTask<bool> CanSensitiveAsync(CancellationToken ct) => permissions.HasPermissionAsync("hr", PositionService.SensitiveAction, ct);

    private async Task<List<EmployeeDetail>> DetailsAsync(IReadOnlyList<Employee> employees, CancellationToken ct)
    {
        var positionIds = employees.Select(e => e.PositionId).Distinct().ToList();
        var positions = await db.JobPositions.IgnoreQueryFilters().AsNoTracking().Where(p => positionIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (p.Name, p.DefaultWorkCenterId), ct);
        var centerIds = positions.Values.Select(p => p.DefaultWorkCenterId).OfType<Guid>().Distinct().ToList();
        var centers = centerIds.Count == 0 ? [] : (await workCenters.FindAsync(centerIds, ct)).ToDictionary(c => c.Id, c => c.Name);
        return employees.Select(e =>
        {
            var (positionName, center) = positions.GetValueOrDefault(e.PositionId, (string.Empty, null));
            return new EmployeeDetail(e.Id, e.Name, e.Registration, e.PositionId, positionName, center, center is { } c ? centers.GetValueOrDefault(c) : null,
                e.WorkScheduleId, e.UnionId, e.AdmissionDate, e.TerminationDate, e.Status);
        }).ToList();
    }

    private static Error NotFound() => Error.NotFound("employee_not_found", "Funcionário não encontrado.");

    private static Error Forbidden() => Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos dados pessoais e de salário. Fale com o administrador.");
}
