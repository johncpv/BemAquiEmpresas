using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

/// <summary>Situação da não conformidade (PRD-05 CA 9): aberta, em ação (causa e ação definidas) e encerrada (com o resultado).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NonConformityStatus>))]
public enum NonConformityStatus
{
    Open,
    InAction,
    Closed,
}

/// <summary>Resultado da decisão de qualidade de uma tentativa (PRD-05 CA 3 e 6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<QualityResult>))]
public enum QualityResult
{
    Approved,
    Rejected,
}

/// <summary>
/// Não conformidade (<c>production.non_conformities</c>, PRD-05 CA 6 a 11): nasce na reprovação da etapa de qualidade, com o número
/// <c>NC-AAAA-0001</c>, a descrição, a causa e a ação (opcionais na abertura), o responsável (padrão: quem reprovou) e a etapa de
/// retorno. O custo estimado é só informativo (ponto 78) e fica fora do diff da auditoria, porque o histórico é de
/// <c>production.view</c> e o custo é de <c>projects.view_cost</c>.
/// </summary>
[DocumentName("A não conformidade")]
public sealed class NonConformity : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; set; } = string.Empty;

    public Guid OrderId { get; init; }

    /// <summary>Etapa de qualidade que reprovou.</summary>
    public Guid OrderStepId { get; init; }

    /// <summary>Tentativa reprovada (1, 2, …).</summary>
    public int Attempt { get; init; }

    public string Description { get; set; } = string.Empty;

    public string? Cause { get; set; }

    public string? Action { get; set; }

    public Guid ResponsibleUserId { get; set; }

    public Guid ReturnToStepId { get; init; }

    public NonConformityStatus Status { get; set; } = NonConformityStatus.Open;

    /// <summary>Custo estimado da NC, informativo (não entra no realizado do projeto).</summary>
    [ProtectedSecret]
    public decimal? Cost { get; set; }

    /// <summary>Resultado do encerramento (até 500).</summary>
    public string? Result { get; set; }

    public Guid? OpenedBy { get; init; }

    public DateTimeOffset OpenedAt { get; init; }

    public Guid? ActionStartedBy { get; set; }

    public DateTimeOffset? ActionStartedAt { get; set; }

    public Guid? ClosedBy { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Decisão de qualidade de uma tentativa (<c>production.quality_decisions</c>, PRD-05 CA 7 e 15): a etapa de qualidade, a tentativa,
/// aprovada ou reprovada, quem decidiu e quando, a marca "Autoinspeção" e a NC da reprovação. A etapa guarda só a última decisão;
/// a aba Qualidade da OS lê o histórico daqui.
/// </summary>
public sealed class QualityDecision : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    public Guid StepId { get; init; }

    public int Attempt { get; init; }

    public QualityResult Result { get; init; }

    public Guid? DecidedBy { get; init; }

    public DateTimeOffset DecidedAt { get; init; }

    public bool SelfInspection { get; init; }

    public Guid? NonConformityId { get; init; }
}
