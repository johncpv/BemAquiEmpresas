using BemAqui.HR.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>
/// Cargo (<c>hr.job_positions</c>, CAD-04a, R-RH-2): CBO, centro de trabalho padrão, jornada e horas mensais; faixa e salário de
/// referência são dados sensíveis (só por rota própria, ADR 0016); o custo-hora é calculado ou ajustado à mão.
/// </summary>
public sealed class JobPosition : Entity, IAuditable, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Classificação Brasileira de Ocupações, 6 dígitos (exibida 9999-99).</summary>
    public string? Cbo { get; set; }

    /// <summary>Só o id do centro (Production), validado por <c>IWorkCenters</c>.</summary>
    public Guid? DefaultWorkCenterId { get; set; }

    public Guid? WorkScheduleId { get; set; }

    /// <summary>Horas mensais contratuais: semanais da jornada × 5, editável (CA 4).</summary>
    public decimal MonthlyHours { get; set; }

    [PersonalData]
    public decimal? SalaryMin { get; set; }

    [PersonalData]
    public decimal? SalaryMax { get; set; }

    [PersonalData]
    public decimal? ReferenceSalary { get; set; }

    /// <summary>Custo-hora com 6 casas (CA 3): calculado pela fórmula ou o valor manual.</summary>
    public decimal HourlyCost { get; set; }

    public bool HourlyCostManual { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Recalcula pela fórmula, a não ser que o custo esteja ajustado à mão; devolve se mudou.</summary>
    public bool Recalculate(decimal chargesPct)
    {
        if (HourlyCostManual)
        {
            return false;
        }

        var cost = HourlyCostCalculator.Compute(ReferenceSalary, chargesPct, MonthlyHours);
        if (cost == HourlyCost)
        {
            return false;
        }

        HourlyCost = cost;
        return true;
    }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>Jornada (<c>hr.work_schedules</c>, CA 15 e 16): os dias em jsonb e as horas semanais derivadas.</summary>
public sealed class WorkSchedule : Entity, IAuditable, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    public List<ScheduleDay> Days { get; private set; } = [];

    public decimal WeeklyHours { get; private set; }

    public bool IsActive { get; set; } = true;

    public string? SeedKey { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Troca os dias e recalcula as horas semanais. Os dias precisam ter passado por <see cref="ScheduleMath.Validate"/>.</summary>
    public void SetDays(IReadOnlyList<ScheduleDay> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        Days = days.OrderBy(d => d.Weekday).ToList();
        WeeklyHours = ScheduleMath.WeeklyHours(Days);
    }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>Sindicato (<c>hr.unions</c>, CA 17): nome, CNPJ e mês da data-base.</summary>
public sealed class LaborUnion : Entity, IAuditable, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;

    public string? Document { get; set; }

    public int BaseMonth { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}
