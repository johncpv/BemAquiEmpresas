using System.Text.Json.Serialization;
using BemAqui.Production.Contracts;
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
/// aberto por funcionário (índice único parcial). Custo (PRD-04): snapshot do custo-hora do cargo no fechamento, valor e versão; os
/// valores ficam fora do diff da auditoria (<see cref="ProtectedSecretAttribute"/>, como o orçado do projeto), porque o histórico da
/// OS é de <c>production.view</c>; o histórico do custo é <see cref="LaborCostRecord"/>, com <c>projects.view_cost</c> (CA 17).
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

    /// <summary>Custo-hora do cargo lido no fechamento (<c>numeric(18,6)</c>, CA 1 e 3); nulo sem custo-hora (CA 5).</summary>
    [ProtectedSecret]
    public decimal? HourlyCostSnapshot { get; set; }

    /// <summary>Minutos líquidos ÷ 60 × snapshot, arredondado no total (<c>numeric(18,2)</c>).</summary>
    [ProtectedSecret]
    public decimal Cost { get; set; }

    /// <summary>"Sem custo-hora" (CA 5): cargo sem custo-hora ou funcionário sem cargo; custo 0 até o recálculo (CA 6).</summary>
    public bool CostMissing { get; set; }

    /// <summary>Versão do custo (0 = ainda sem custo, apontamento aberto ou fechado antes da PRD-04): o <c>seq</c> da razão.</summary>
    public int CostVersion { get; set; }

    /// <summary>"Calculado na implantação" (D3, CA 12 e 13): custo-hora do dia da recomposição.</summary>
    public bool CostBackfilled { get; set; }

    /// <summary>Cargo do funcionário no cálculo do custo (aviso e recálculo por cargo, CA 5 e 6).</summary>
    public Guid? CostPositionId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }
}

/// <summary>
/// Histórico do custo do apontamento (<c>production.labor_cost_history</c>, PRD-04 CA 17): uma linha por versão, só inserida (nunca
/// alterada nem apagada), com o snapshot, as horas, o valor, a origem e quem fez. Lida só com <c>projects.view_cost</c>.
/// </summary>
public sealed class LaborCostRecord : Entity
{
    public Guid LaborEntryId { get; init; }

    public Guid OrderId { get; init; }

    public int CostVersion { get; init; }

    public LaborCostOrigin Origin { get; init; }

    public int Minutes { get; init; }

    public decimal? HourlyCostSnapshot { get; init; }

    public decimal PreviousAmount { get; init; }

    public decimal Amount { get; init; }

    public bool CostMissing { get; init; }

    /// <summary>Usuário do comando; nulo no job de recomposição.</summary>
    public Guid? RecordedBy { get; init; }

    public DateTimeOffset RecordedAt { get; init; }
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