using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.HR.Contracts;
using BemAqui.HR.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.HR.Application;

/// <summary>
/// Cargos (CAD-04a CA 1 a 14). Três comandos separados (ADR 0016): dados gerais, faixa salarial (sensível) e ajuste do
/// custo-hora. A lista e o detalhe nunca trazem salário; a faixa só sai por <see cref="GetSalaryAsync"/>, com <c>SensitiveRead</c>.
/// </summary>
public sealed partial class PositionService(
    IHrDb db,
    ITenantParameters parameters,
    IWorkCenters workCenters,
    IPermissionChecker permissions,
    IAuditWriter audit,
    IEventPublisher events,
    IEnumerable<IPositionUsageProvider> usages,
    TimeProvider clock) : IHourlyCost
{
    public const string SensitiveAction = "view_sensitive";

    /// <summary>Lista ordenada só por nome ou custo-hora: ordenação ou filtro por salário são ignorados (CA 12).</summary>
    public async Task<PagedList<PositionDetail>> ListAsync(PositionListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var positions = db.JobPositions.AsNoTracking().Where(p => query.IncludeInactive || p.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = "%" + query.Q.Trim() + "%";
            positions = positions.Where(p => EF.Functions.ILike(p.Name, term));
        }

        positions = page.ParseSort() switch
        {
            ("hourlyCost", true) => positions.OrderByDescending(p => p.HourlyCost),
            ("hourlyCost", false) => positions.OrderBy(p => p.HourlyCost),
            ("name", true) => positions.OrderByDescending(p => p.Name),
            _ => positions.OrderBy(p => p.Name),
        };
        var paged = await positions.ToPagedListAsync(page, cancellationToken);
        var details = await DetailsAsync(paged.Items, cancellationToken);
        return new PagedList<PositionDetail>(details, paged.Page, paged.PageSize, paged.Total);
    }

    public async Task<Result<PositionDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var position = await db.JobPositions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return position is null ? NotFound() : (await DetailsAsync([position], cancellationToken))[0];
    }

    /// <summary>Faixa e salário de referência (CA 10): a rota já exige <c>hr.view_sensitive</c>; a leitura fica na auditoria.</summary>
    public async Task<Result<PositionSalaryDetail>> GetSalaryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var position = await db.JobPositions.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (position is null)
        {
            return NotFound();
        }

        await audit.RecordReadAsync(nameof(JobPosition), id.ToString(), cancellationToken);
        return new PositionSalaryDetail(position.Id, position.SalaryMin, position.SalaryMax, position.ReferenceSalary);
    }

    public async Task<PositionSummary> SummaryAsync(CancellationToken cancellationToken = default) =>
        new(await db.JobPositions.CountAsync(p => p.IsActive, cancellationToken), await db.JobPositions.CountAsync(p => p.IsActive && !p.HourlyCostManual, cancellationToken),
            await parameters.PayrollChargesPctAsync(cancellationToken));

    public async Task<IReadOnlyList<WorkCenterChoice>> WorkCenterChoicesAsync(CancellationToken cancellationToken = default) =>
        (await workCenters.ActiveAsync(cancellationToken)).Select(c => new WorkCenterChoice(c.Id, c.Name)).ToList();

    public async Task<Result<PositionDetail>> CreateAsync(PositionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var position = new JobPosition { CreatedAt = clock.GetUtcNow() };
        if (await ApplyAsync(position, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.JobPositions.Add(position);
        return await SaveAsync(position, cancellationToken);
    }

    public async Task<Result<PositionDetail>> UpdateAsync(Guid id, PositionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var position = await db.JobPositions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (position is null)
        {
            return NotFound();
        }

        return await ApplyAsync(position, input, cancellationToken) is { } invalid ? invalid : await SaveAsync(position, cancellationToken);
    }

    /// <summary>Faixa salarial (CA 2 e 11): exige também <c>hr.view_sensitive</c>; o salário de referência sem valor vira a média.</summary>
    public async Task<Result<PositionDetail>> UpdateSalaryAsync(Guid id, PositionSalaryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("hr", SensitiveAction, cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso aos dados de salário. Fale com o administrador.");
        }

        var position = await db.JobPositions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (position is null)
        {
            return NotFound();
        }

        foreach (var (field, value) in new[] { ("salaryMin", input.SalaryMin), ("salaryMax", input.SalaryMax), ("referenceSalary", input.ReferenceSalary) })
        {
            if (value is { } v && (v < 0 || decimal.Round(v, 2) != v || v >= 10_000_000_000m))
            {
                return Invalid(field, "Informe um valor de zero para cima, com até 2 casas.");
            }
        }

        if (input.SalaryMin is { } min && input.SalaryMax is { } max && min > max)
        {
            return Invalid("salaryMax", "O salário máximo precisa ser maior ou igual ao mínimo.");
        }

        position.SalaryMin = input.SalaryMin;
        position.SalaryMax = input.SalaryMax;
        position.ReferenceSalary = input.ReferenceSalary ?? HourlyCostCalculator.Average(input.SalaryMin, input.SalaryMax);
        return await SaveAsync(position, cancellationToken);
    }

    /// <summary>Ajuste manual do custo-hora (CA 5); null volta ao calculado.</summary>
    public async Task<Result<PositionDetail>> SetManualCostAsync(Guid id, decimal? value, CancellationToken cancellationToken = default)
    {
        var position = await db.JobPositions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (position is null)
        {
            return NotFound();
        }

        if (value is { } v)
        {
            if (v < 0 || decimal.Round(v, 6) != v || v >= 1_000_000m)
            {
                return Invalid("value", "Informe o custo-hora de zero para cima, com até 6 casas.");
            }

            position.HourlyCostManual = true;
            position.HourlyCost = v;
        }
        else
        {
            position.HourlyCostManual = false;
        }

        return await SaveAsync(position, cancellationToken);
    }

    public async Task<Result<PositionDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var position = await db.JobPositions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (position is null)
        {
            return NotFound();
        }

        position.IsActive = active;
        return await SaveAsync(position, cancellationToken);
    }

    /// <summary>Excluir (CA 9): com funcionário ativo, só inativa.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var position = await db.JobPositions.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (position is null)
        {
            return NotFound();
        }

        foreach (var usage in usages)
        {
            if (await usage.HasEmployeesAsync(id, cancellationToken))
            {
                return Error.Conflict("position_in_use", "O cargo tem funcionários. Inative o cargo ou mude o cargo deles.");
            }
        }

        db.JobPositions.Remove(position);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Recalcula os cargos não ajustados à mão com o novo % (CA 6); cada mudança vai para o histórico e publica o evento.</summary>
    public async Task<int> RecalculateAllAsync(decimal chargesPct, CancellationToken cancellationToken = default)
    {
        var changed = 0;
        foreach (var position in await db.JobPositions.Where(p => !p.HourlyCostManual).ToListAsync(cancellationToken))
        {
            if (position.Recalculate(chargesPct))
            {
                events.Publish(new HourlyCostChanged(position.Id));
                changed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    public async Task<decimal?> ForPositionAsync(Guid positionId, CancellationToken cancellationToken = default) =>
        await db.JobPositions.IgnoreQueryFilters().Where(p => p.Id == positionId).Select(p => (decimal?)p.HourlyCost).FirstOrDefaultAsync(cancellationToken);

    private async Task<Error?> ApplyAsync(JobPosition position, PositionInput input, CancellationToken ct)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length is 0 or > 100)
        {
            return Invalid("name", "Informe o nome do cargo, com até 100 caracteres.");
        }

        if (await db.JobPositions.AnyAsync(p => p.Id != position.Id && p.Name == name, ct))
        {
            return Error.Conflict("position_name_taken", $"Já existe o cargo {name}.");
        }

        var cbo = new string((input.Cbo ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        if (cbo.Length is not (0 or 6))
        {
            return Invalid("cbo", "O CBO tem 6 dígitos (ex.: 7244-15).");
        }

        if (input.DefaultWorkCenterId is { } center && center != position.DefaultWorkCenterId
            && !(await workCenters.ActiveAsync(ct)).Any(c => c.Id == center))
        {
            return Invalid("defaultWorkCenterId", "Escolha um centro de trabalho ativo da lista.");
        }

        WorkSchedule? schedule = null;
        if (input.WorkScheduleId is { } sid && (schedule = await db.WorkSchedules.FirstOrDefaultAsync(s => s.Id == sid && (s.IsActive || s.Id == position.WorkScheduleId), ct)) is null)
        {
            return Invalid("workScheduleId", "Escolha uma jornada ativa da lista.");
        }

        var hours = input.MonthlyHours ?? (schedule is null ? position.MonthlyHours : ScheduleMath.MonthlyHours(schedule.WeeklyHours));
        if (hours is < 0 or > 744 || decimal.Round(hours, 2) != hours)
        {
            return Invalid("monthlyHours", "Informe as horas mensais de 0 a 744, com até 2 casas.");
        }

        position.Name = name;
        position.Cbo = cbo.Length == 0 ? null : cbo;
        position.DefaultWorkCenterId = input.DefaultWorkCenterId;
        position.WorkScheduleId = input.WorkScheduleId;
        position.MonthlyHours = hours;
        return null;
    }

    private async Task<Result<PositionDetail>> SaveAsync(JobPosition position, CancellationToken ct)
    {
        if (position.Recalculate(await parameters.PayrollChargesPctAsync(ct)))
        {
            events.Publish(new HourlyCostChanged(position.Id));
        }

        await db.SaveChangesAsync(ct);
        return (await DetailsAsync([position], ct))[0];
    }

    private async Task<List<PositionDetail>> DetailsAsync(IReadOnlyList<JobPosition> positions, CancellationToken ct)
    {
        var centerIds = positions.Select(p => p.DefaultWorkCenterId).OfType<Guid>().Distinct().ToList();
        var centers = centerIds.Count == 0 ? [] : (await workCenters.FindAsync(centerIds, ct)).ToDictionary(c => c.Id, c => c.Name);
        var scheduleIds = positions.Select(p => p.WorkScheduleId).OfType<Guid>().Distinct().ToList();
        var schedules = await db.WorkSchedules.IgnoreQueryFilters().AsNoTracking().Where(s => scheduleIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var ids = positions.Select(p => p.Id).ToList();
        var counts = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.PositionId) && e.Status == EmployeeStatus.Active)
            .GroupBy(e => e.PositionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        return positions.Select(p => new PositionDetail(
            p.Id, p.Name, p.Cbo, p.DefaultWorkCenterId, p.DefaultWorkCenterId is { } c ? centers.GetValueOrDefault(c) : null,
            p.WorkScheduleId, p.WorkScheduleId is { } s ? schedules.GetValueOrDefault(s) : null,
            p.MonthlyHours, p.HourlyCost, p.HourlyCostManual, p.IsActive, p.CreatedAt, counts.GetValueOrDefault(p.Id))).ToList();
    }

    private static Error NotFound() => Error.NotFound("position_not_found", "Cargo não encontrado.");

    public static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });
}
