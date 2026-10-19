using System.Text.Json.Serialization;
using BemAqui.HR.Contracts;

namespace BemAqui.HR.Application;

/// <summary>
/// Dados gerais do cargo (CA 1 e 4). Comando separado do salário (ADR 0016): campo de salário no corpo responde 400 e nunca
/// anula a faixa gravada (CA 11). Sem horas mensais, valem as semanais da jornada × 5.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PositionInput(string Name, string? Cbo, Guid? DefaultWorkCenterId, Guid? WorkScheduleId, decimal? MonthlyHours);

/// <summary>Faixa salarial (CA 2), comando próprio com <c>hr.view_sensitive</c>. Sem salário de referência, vale a média da faixa.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PositionSalaryInput(decimal? SalaryMin, decimal? SalaryMax, decimal? ReferenceSalary);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HourlyCostInput(decimal Value);

/// <summary>Cargo para a lista e o detalhe: nunca traz a faixa nem o salário (CA 10); o custo-hora do cargo aparece com <c>hr.view</c>.</summary>
public sealed record PositionDetail(
    Guid Id,
    string Name,
    string? Cbo,
    Guid? DefaultWorkCenterId,
    string? DefaultWorkCenterName,
    Guid? WorkScheduleId,
    string? WorkScheduleName,
    decimal MonthlyHours,
    decimal HourlyCost,
    bool HourlyCostManual,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>Só pela rota <c>/sensitive</c>, com <c>hr.view_sensitive</c>, <c>no-store</c> e <c>SensitiveRead</c> (CA 10).</summary>
public sealed record PositionSalaryDetail(Guid Id, decimal? SalaryMin, decimal? SalaryMax, decimal? ReferenceSalary);

/// <summary>Para o diálogo do % de encargos (CA 7) e a prévia do custo-hora: contagens e o % vigente, sem salário.</summary>
public sealed record PositionSummary(int Total, int Calculated, decimal ChargesPct);

/// <summary>Centro de trabalho ativo para o seletor do cargo (CA 1), lido por <c>IWorkCenters</c>.</summary>
public sealed record WorkCenterChoice(Guid Id, string Name);

public sealed record PositionListQuery(string? Q, bool IncludeInactive);

public sealed record ScheduleInput(string Name, IReadOnlyList<ScheduleDay> Days);

public sealed record ScheduleDetail(Guid Id, string Name, IReadOnlyList<ScheduleDay> Days, decimal WeeklyHours, decimal MonthlyHours, bool IsActive, string? SeedKey);

public sealed record UnionInput(string Name, string? Document, int BaseMonth);

public sealed record UnionDetail(Guid Id, string Name, string? Document, int BaseMonth, bool IsActive);
