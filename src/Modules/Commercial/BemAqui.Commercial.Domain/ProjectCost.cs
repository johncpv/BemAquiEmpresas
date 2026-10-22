using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Commercial.Domain;

/// <summary>
/// Fonte de uma linha da razão de custo do projeto (ADR 0025 §2): horas apontadas (PRD-04) e, na EST-03, a baixa e a devolução de
/// material para a OS. Os movimentos <c>Production</c> e <c>Shipment</c> nunca entram (§1).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProjectCostSource>))]
public enum ProjectCostSource
{
    StockIssue,
    StockReturn,
    Labor,
    LaborCorrection,
}

/// <summary>
/// Razão de diferenças do custo do projeto (<c>commercial.project_costs</c>, ADR 0025 §2, PRD-04 CA 7 e 8): cada linha é a diferença
/// entre o valor novo da fonte e o que já foi gravado para ela (<c>amount</c> com sinal); a soma das linhas de uma fonte é sempre o
/// valor atual dela. Só inserida, nunca alterada nem apagada; única por <c>(source, source_id, seq)</c>. Só o Commercial grava.
/// </summary>
public sealed class ProjectCost : Entity
{
    public Guid ProjectId { get; init; }

    public Guid? ProductionOrderId { get; init; }

    public CostType CostType { get; init; }

    public ProjectCostSource Source { get; init; }

    /// <summary>Id do documento da fonte (o apontamento, a requisição).</summary>
    public Guid SourceId { get; init; }

    /// <summary>Versão do valor da fonte (no apontamento, <c>cost_version</c>).</summary>
    public int Seq { get; init; }

    public decimal Amount { get; init; }

    public DateOnly OccurredOn { get; init; }

    public DateTimeOffset RecordedAt { get; init; }

    /// <summary>Recomposição do que foi publicado antes do consumidor (D3): marca "Calculado na implantação".</summary>
    public bool IsBackfill { get; init; }
}