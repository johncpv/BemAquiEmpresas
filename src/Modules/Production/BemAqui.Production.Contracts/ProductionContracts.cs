using System.Text.Json.Serialization;

namespace BemAqui.Production.Contracts;

/// <summary>Turno de um centro (CAD-03 CA 12): início e fim no horário local, "HH:mm", sem virar o dia.</summary>
public sealed record Shift(string Start, string End);

/// <summary>Tipo de exceção do calendário (CA 9): feriado ou parada; as horas disponíveis dizem quanto sobra no dia.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CalendarExceptionType>))]
public enum CalendarExceptionType
{
    Holiday,
    Stoppage,
}

/// <summary>Dia da semana do calendário (CA 8), na mesma ordem de <see cref="DayOfWeek"/>, em texto na API.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Weekday>))]
public enum Weekday
{
    Sunday,
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
}

/// <summary>Centro de trabalho para os seletores dos outros módulos (CA 18).</summary>
public sealed record WorkCenterOption(Guid Id, string Name, string? Line, decimal HoursPerDay, bool IsActive);

/// <summary>Centros de trabalho para HR, Engineering e Production (ADR 0016): só ativos nos seletores, inativos por id.</summary>
public interface IWorkCenters
{
    Task<IReadOnlyList<WorkCenterOption>> ActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Por id, inclusive inativos; os excluídos não voltam.</summary>
    Task<IReadOnlyList<WorkCenterOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}

/// <summary>Horas disponíveis de um centro num dia (data local).</summary>
public sealed record DailyCapacity(DateOnly Date, decimal Hours);

public sealed record WorkCenterCapacity(Guid WorkCenterId, IReadOnlyList<DailyCapacity> Days, decimal TotalHours);

/// <summary>
/// Capacidade diária para o planejamento (CA 15 e 16, ADR 0016): por id, lida na hora (sem evento de mudança de calendário).
/// Dia = 0 se o centro está inativo, ainda não existia ou o dia não é útil; senão o menor entre as horas da exceção e as
/// horas por dia; senão as horas por dia.
/// </summary>
public interface IWorkCenterCapacity
{
    /// <summary>De <paramref name="from"/> a <paramref name="to"/>, inclusive (até 2 anos); null se o centro não existe.</summary>
    Task<WorkCenterCapacity?> GetAvailableHoursAsync(Guid workCenterId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>Quem usa um centro (CA 14): cargo (HR), roteiro (Engineering), OS (Production). Em uso, só inativa.</summary>
public interface IWorkCenterUsageProvider
{
    /// <summary>Descrição curta do uso ("cargos") ou null se não usa.</summary>
    Task<string?> UsageAsync(Guid workCenterId, CancellationToken cancellationToken = default);
}

/// <summary>Instalação iniciada (COM-06 CA 6): o Commercial passa o projeto a Instalando.</summary>
public sealed record InstallationStarted(Guid ProductionOrderId, Guid ProjectId, DateOnly StartedOn) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>Aceite técnico da OS (COM-06 CA 9, 10): com todas as OS do projeto aceitas, o projeto é entregue.</summary>
public sealed record InstallationAccepted(Guid ProductionOrderId, Guid ProjectId, DateOnly AcceptedOn) : BemAqui.SharedKernel.IIntegrationEvent;

[JsonConverter(typeof(JsonStringEnumConverter<InstallationState>))]
public enum InstallationState
{
    Open,
    InProgress,
    Completed,
    Cancelled,
}

public sealed record InstallationOrderInfo(Guid Id, string Number, InstallationState Status, DateOnly PlannedStart, DateOnly PlannedEnd, IReadOnlyList<Guid> ProjectItemIds);

/// <summary>OS de instalação para os outros módulos (COM-06): Commercial (entrega e projeto 360) e Finance (título órfão).</summary>
public interface IInstallationOrders
{
    Task<IReadOnlyList<InstallationOrderInfo>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Situação da OS; null se não existe.</summary>
    Task<InstallationState?> StatusAsync(Guid orderId, CancellationToken cancellationToken = default);
}
