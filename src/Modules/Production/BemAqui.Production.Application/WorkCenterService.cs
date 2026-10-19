using BemAqui.BuildingBlocks.Paging;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Centros de trabalho (CAD-03 CA 12 a 14). Horas por dia derivadas dos turnos, calendário padrão do tenant, inativar e
/// excluir conforme o uso informado pelos <see cref="IWorkCenterUsageProvider"/> (D10).
/// </summary>
public sealed class WorkCenterService(IProductionDb db, CalendarService calendars, IEnumerable<IWorkCenterUsageProvider> usages, TimeProvider clock)
{
    public async Task<PagedList<WorkCenterDetail>> ListAsync(WorkCenterListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var centers = db.WorkCenters.AsNoTracking().Where(w => query.IncludeInactive || w.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = "%" + query.Q.Trim() + "%";
            centers = centers.Where(w => EF.Functions.ILike(w.Name, term) || (w.Line != null && EF.Functions.ILike(w.Line, term)));
        }

        var result = await centers.OrderBy(w => w.Name).ToPagedListAsync(page, cancellationToken);
        return new PagedList<WorkCenterDetail>(result.Items.Select(ToDetail).ToList(), result.Page, result.PageSize, result.Total);
    }

    public async Task<Result<WorkCenterDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var center = await db.WorkCenters.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        return center is null ? NotFound() : ToDetail(center);
    }

    /// <summary>Turnos do último centro cadastrado, como sugestão para o próximo (propositivo); vazio sem centros.</summary>
    public async Task<IReadOnlyList<Shift>> SuggestedShiftsAsync(CancellationToken cancellationToken = default) =>
        (await db.WorkCenters.AsNoTracking().OrderByDescending(w => w.CreatedAt).Select(w => w.Shifts).FirstOrDefaultAsync(cancellationToken)) ?? [];

    public async Task<Result<WorkCenterDetail>> CreateAsync(WorkCenterInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await ValidateAsync(input, null, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var calendar = await calendars.DefaultEntityAsync(cancellationToken);
        var center = new WorkCenter { CreatedAt = clock.GetUtcNow(), CalendarId = calendar.Id };
        Apply(center, input);
        db.WorkCenters.Add(center);
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(center);
    }

    public async Task<Result<WorkCenterDetail>> UpdateAsync(Guid id, WorkCenterInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var center = await db.WorkCenters.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (center is null)
        {
            return NotFound();
        }

        if (await ValidateAsync(input, id, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        Apply(center, input);
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(center);
    }

    /// <summary>Inativar ou reativar (CA 14): inativo some dos seletores e a capacidade dele passa a 0.</summary>
    public async Task<Result<WorkCenterDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var center = await db.WorkCenters.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (center is null)
        {
            return NotFound();
        }

        center.IsActive = active;
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(center);
    }

    /// <summary>Exclusão lógica só sem uso (CA 14).</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var center = await db.WorkCenters.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (center is null)
        {
            return NotFound();
        }

        foreach (var usage in usages)
        {
            if (await usage.UsageAsync(id, cancellationToken) is not null)
            {
                return Error.Conflict("work_center_in_use", "O centro está em uso. Inative-o.");
            }
        }

        db.WorkCenters.Remove(center);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> ValidateAsync(WorkCenterInput input, Guid? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100)
        {
            return Invalid("name", "Informe o nome do centro, com até 100 caracteres.");
        }

        if (input.Line is { Length: > 60 })
        {
            return Invalid("line", "A linha tem até 60 caracteres.");
        }

        if (ShiftMath.Validate(input.Shifts) is { } message)
        {
            return Invalid("shifts", message);
        }

        var name = input.Name.Trim();
        // Mesmo nome sem diferenciar maiúsculas; % e _ do nome valem como texto.
        var pattern = name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        return await db.WorkCenters.AnyAsync(w => w.Id != id && EF.Functions.ILike(w.Name, pattern, "\\"), cancellationToken)
            ? Error.Conflict("work_center_name_taken", $"Já existe o centro {name}.")
            : null;
    }

    private static void Apply(WorkCenter center, WorkCenterInput input)
    {
        center.Name = input.Name.Trim();
        center.Line = string.IsNullOrWhiteSpace(input.Line) ? null : input.Line.Trim();
        center.SetShifts(input.Shifts);
    }

    public static WorkCenterDetail ToDetail(WorkCenter w) => new(w.Id, w.Name, w.Line, w.Shifts, w.HoursPerDay, w.CalendarId, w.IsActive, w.CreatedAt);

    private static Error NotFound() => Error.NotFound("work_center_not_found", "Centro de trabalho não encontrado.");

    internal static Error Invalid(string field, string message) =>
        Error.Validation("validation", message, new Dictionary<string, string[]> { [field] = [message] });
}
