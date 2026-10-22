using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

/// <summary>Como o apontamento fechou (PRD-03a): pelo operador, saindo da etapa, pelo supervisor ou com a etapa concluída por outro.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LaborCloseKind>))]
public enum LaborCloseKind
{
    Operator,
    Left,
    Supervisor,
    StepCompleted,
}

/// <summary>Motivo da pausa (PRD-03a CA 11): falta de material avisa o almoxarifado; "Outro" tem texto opcional.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PauseReason>))]
public enum PauseReason
{
    Material,
    Machine,
    Break,
    EndOfShift,
    Other,
}

/// <summary>
/// Apontamento de um funcionário numa etapa da OS (<c>production.labor_entries</c>, PRD-03a): início, fim e pausas. O funcionário
/// é sempre o do usuário que apontou, exceto no "Apontar por funcionário" do supervisor (<see cref="OnBehalf"/>). Um apontamento
/// aberto por funcionário (índice único parcial). Sem custo: a PRD-04 grava o custo-hora e o valor.
/// </summary>
[DocumentName("O apontamento")]
public sealed class LaborEntry : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid OrderId { get; init; }

    public Guid OrderStepId { get; init; }

    public Guid EmployeeId { get; init; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    /// <summary>Σ das pausas, em minutos, gravado no fechamento.</summary>
    public int PausedMinutes { get; set; }

    /// <summary>Usuário que apontou (D11: fica registrado quem apontou).</summary>
    public Guid? RecordedBy { get; init; }

    /// <summary>"Apontado por {supervisor}" (CA 18).</summary>
    public bool OnBehalf { get; init; }

    public Guid? ClosedBy { get; set; }

    public LaborCloseKind? CloseKind { get; set; }

    public DateTimeOffset? CorrectedAt { get; set; }

    public Guid? CorrectedBy { get; set; }

    public string? CorrectionReason { get; set; }

    /// <summary>Correção feita por quem é o dono do apontamento, sem outro aprovador elegível (CA 21, "Autoaprovado").</summary>
    public bool SelfApproved { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }
}

/// <summary>Pausa de um apontamento (<c>production.step_pauses</c>, CA 11 e 12): uma aberta por apontamento.</summary>
public sealed class StepPause : Entity, IAuditable
{
    public Guid LaborEntryId { get; init; }

    /// <summary>OS do apontamento, para o histórico da OS.</summary>
    public Guid OrderId { get; init; }

    public PauseReason Reason { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }
}