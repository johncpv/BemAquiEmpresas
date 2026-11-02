using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

/// <summary>Motivo da parada do centro (PAT-03b): por enquanto só a manutenção; outras paradas (falta de material) entram depois.</summary>
public enum DowntimeReason
{
    Maintenance,
}

/// <summary>
/// Parada de um centro de trabalho (<c>production.downtimes</c>, PAT-03b, ADR 0038 §4): dona a Produção, escrita só pelo consumidor dos eventos
/// da manutenção, uma por origem (<c>(source_type, source_id)</c> único). Desconta <see cref="HoursPerDay"/> da capacidade do centro de
/// <see cref="StartsOn"/> até o fim efetivo (<see cref="DowntimeRules.EffectiveEnd"/>); só datas locais.
/// </summary>
[DocumentName("Parada do centro")]
public sealed class Downtime : Entity, IAuditable
{
    public const string MaintenanceSource = "MaintenanceOrder";

    public Guid WorkCenterId { get; set; }

    /// <summary>Bem parado (Patrimônio), só o id.</summary>
    public Guid? AssetId { get; set; }

    public DowntimeReason Reason { get; init; } = DowntimeReason.Maintenance;

    public string SourceType { get; init; } = MaintenanceSource;

    public Guid SourceId { get; init; }

    public DateOnly StartsOn { get; set; }

    /// <summary>Fim previsto (inclusive).</summary>
    public DateOnly PlannedEndOn { get; set; }

    /// <summary>Fim real (conclusão ou cancelamento depois do início), inclusive.</summary>
    public DateOnly? EndedOn { get; set; }

    /// <summary>Máquina parada: sem fim real, a parada continua até hoje mesmo depois do fim previsto.</summary>
    public bool OpenEnded { get; set; }

    /// <summary>Horas paradas por dia (<c>numeric(18,4)</c>).</summary>
    public decimal HoursPerDay { get; set; }

    /// <summary>Revisão da origem aplicada por último (o evento mais velho não volta atrás).</summary>
    public int SourceRevision { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Regras puras das paradas (PAT-03b CA 8 a 10).</summary>
public static class DowntimeRules
{
    /// <summary>Fim efetivo: o real; sem ele, o previsto, ou hoje se a máquina segue parada depois do previsto.</summary>
    public static DateOnly EffectiveEnd(DateOnly plannedEndOn, DateOnly? endedOn, bool openEnded, DateOnly today) =>
        endedOn ?? (openEnded && today > plannedEndOn ? today : plannedEndOn);

    /// <summary>A parada vale no dia (do início ao fim efetivo, inclusive).</summary>
    public static bool Covers(DateOnly startsOn, DateOnly plannedEndOn, DateOnly? endedOn, bool openEnded, DateOnly date, DateOnly today) =>
        date >= startsOn && date <= EffectiveEnd(plannedEndOn, endedOn, openEnded, today);

    /// <summary>
    /// Capacidade do dia com as paradas descontadas (CA 8): as horas do dia menos a soma das horas paradas por dia das paradas que valem
    /// nele, nunca abaixo de zero. Dia sem capacidade fica zero.
    /// </summary>
    public static decimal Discount(decimal dayHours, decimal stoppedHours) => dayHours <= 0 ? 0m : Math.Max(0m, dayHours - Math.Max(0m, stoppedHours));
}
