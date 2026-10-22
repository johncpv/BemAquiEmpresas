using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Contracts;

/// <summary>Dia da jornada (CAD-04a CA 15): dia da semana ISO (1 = segunda … 7 = domingo), entrada e saída "HH:mm" e intervalo.</summary>
public sealed record ScheduleDay(int Weekday, string Start, string End, int BreakMinutes);

/// <summary>
/// Custo-hora para Production (PRD-04) e Commercial (COM-01), só por cargo (CA 8): não existe custo por funcionário e o
/// contrato não exige permissão de RH do usuário. Null se o cargo não existe.
/// </summary>
public interface IHourlyCost
{
    Task<decimal?> ForPositionAsync(Guid positionId, CancellationToken cancellationToken = default);
}

/// <summary>Custo-hora de um cargo mudou (CA 6), sem valores; sem consumidor nesta fase.</summary>
public sealed record HourlyCostChanged(Guid PositionId) : IIntegrationEvent;

/// <summary>Quem usa um cargo (CAD-04b): com funcionário ativo, o cargo não é excluído, só inativado (CA 9).</summary>
public interface IPositionUsageProvider
{
    Task<bool> HasEmployeesAsync(Guid positionId, CancellationToken cancellationToken = default);
}

/// <summary>Quem usa um sindicato (CAD-04b): em uso, só inativa (CA 17).</summary>
public interface IUnionUsageProvider
{
    Task<bool> InUseAsync(Guid unionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Situação do funcionário para os outros módulos (CAD-04c1 CA 3): <c>OnLeave</c> é calculado na leitura pelo afastamento
/// vigente (hoje em São Paulo) e <c>Terminated</c> prevalece. Sem tipo, motivo nem datas do afastamento.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EmployeeSituation>))]
public enum EmployeeSituation
{
    Active,
    OnLeave,
    Terminated,
}

/// <summary>
/// Funcionário para Production e Assets (CAD-04b CA 21): só o que não é sensível. O custo-hora vem de <see cref="IHourlyCost"/>,
/// por cargo. <c>IsActive</c>: não desligado nem excluído (o afastado continua ativo); <c>Status</c>: a situação de hoje, pela qual
/// o apontamento recusa afastado ou desligado (PRD-03a).
/// </summary>
public sealed record EmployeeSummary(Guid Id, string Name, string? Registration, Guid PositionId, string PositionName, Guid? WorkCenterId, bool IsActive, EmployeeSituation Status);

public interface IEmployees
{
    Task<IReadOnlyList<EmployeeSummary>> ActiveAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeSummary>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>Desligamento registrado (CA 17), sem consumidor nesta fase.</summary>
public sealed record EmployeeTerminated(Guid EmployeeId, DateOnly TerminationDate) : IIntegrationEvent;

/// <summary>Salário mudou (CA 14), só o id e a vigência, sem valor; sem consumidor nesta fase.</summary>
public sealed record EmployeeSalaryChanged(Guid EmployeeId, DateOnly EffectiveDate) : IIntegrationEvent;

/// <summary>Afastamento começou (CAD-04c1 CA 6 e 7): só o id do funcionário e as datas, nunca o tipo nem o motivo.</summary>
public sealed record EmployeeLeaveStarted(Guid EmployeeId, DateOnly StartDate, DateOnly ExpectedEndDate) : IIntegrationEvent;

/// <summary>Afastamento terminou (no dia seguinte ao fim previsto, CA 7): só o id do funcionário e as datas.</summary>
public sealed record EmployeeLeaveEnded(Guid EmployeeId, DateOnly StartDate, DateOnly EndDate) : IIntegrationEvent;
