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
