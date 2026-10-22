using System.Text.Json.Serialization;
using BemAqui.Production.Domain;

namespace BemAqui.Production.Application;

/// <summary>Filtros do Kanban na URL (PRD-02 CA 2 e 6): projeto, cliente, linha, roteiro, atrasadas e em risco.</summary>
public sealed record ProductionBoardQuery(Guid? ProjectId, Guid? PartyId, string? Line, Guid? RoutingTemplateId, bool Late, bool AtRisk);

/// <summary>Tipo da coluna do Kanban: código de etapa, "Sem roteiro" ou "Pronta para concluir" (D12).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BoardColumnKind>))]
public enum BoardColumnKind
{
    Step,
    NoRouting,
    ReadyToComplete,
}

/// <summary>Coluna (CA 1): a letra (display de andar), o nome, a quantidade de OS e as horas restantes das etapas atuais.</summary>
public sealed record BoardColumn(string Key, BoardColumnKind Kind, string? Code, string Name, int Count, decimal RemainingHours);

/// <summary>
/// Cartão (CA 3): OS, produto, projeto, cliente, etapa atual com horas apontadas × padrão, prometido, atraso ou risco e os chips.
/// Sem custo. Apontadas, operador e pausa ficam vazios até a PRD-03a.
/// </summary>
public sealed record BoardCard(
    Guid Id, string Number, ProductionOrderStatus Status, string ProductName, decimal Quantity, Guid ProjectId, string ProjectNumber, string ProjectName,
    Guid PartyId, string PartyName, string ColumnKey, Guid? CurrentStepId, string? CurrentStepCode, string? CurrentStepName, ProductionStepStatus? CurrentStepStatus,
    bool CurrentStepIsQuality, decimal StandardHours, decimal AppliedHours, string? Line, IReadOnlyList<string> Codes, IReadOnlyList<string> StepNames,
    int CurrentStepIndex, DateOnly? PromisedDate, DeliveryRisk Risk, int? DelayDays, bool ReadyToComplete, bool NoRouting, bool InExecution, string? PausedReason,
    string? OperatorInitials, Guid? RoutingTemplateId, uint Version);

public sealed record BoardOption(Guid Id, string Label);

/// <summary>
/// Quadro inteiro numa resposta (CA 14): colunas, cartões, contadores de atraso e risco (que também filtram, CA 6) e as opções
/// dos filtros, tiradas das OS abertas.
/// </summary>
public sealed record ProductionBoard(
    IReadOnlyList<BoardColumn> Columns, IReadOnlyList<BoardCard> Cards, int LateCount, int AtRiskCount, int OpenCount, IReadOnlyList<BoardOption> Projects,
    IReadOnlyList<BoardOption> Parties, IReadOnlyList<BoardOption> Routings, IReadOnlyList<string> Lines);

/// <summary>"Concluir etapa" do supervisor (CA 8): motivo obrigatório e a versão da OS que o cartão mostrou.</summary>
public sealed record CompleteStepInput(string? Reason, uint Version);

/// <summary>Etapa vista pelo quadro, para montar as colunas sem banco.</summary>
public sealed record BoardStep(string? Code, string Name, int Sequence, ProductionStepStatus? Status);

/// <summary>Regras do quadro sem banco (PRD-02 CA 1 e 2): etapa atual, coluna do cartão e colunas pelo código da etapa.</summary>
public static class BoardRules
{
    public const string NoRoutingKey = "sem-roteiro";
    public const string ReadyKey = "pronta";
    public const string NoRoutingName = "Sem roteiro";
    public const string ReadyName = "Pronta para concluir";

    /// <summary>Etapa atual = a de menor sequência não <c>Done</c>; -1 sem etapas ou com todas concluídas (D12).</summary>
    public static int CurrentIndex(IReadOnlyList<BoardStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Status != ProductionStepStatus.Done)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Coluna do cartão: o código da etapa atual; "Sem roteiro" sem etapas; "Pronta para concluir" com todas concluídas.</summary>
    public static string ColumnKey(IReadOnlyList<BoardStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            return NoRoutingKey;
        }

        var current = CurrentIndex(steps);
        return current < 0 ? ReadyKey : Key(steps[current]);
    }

    /// <summary>
    /// Colunas pelo código (CA 2, D10): os códigos distintos das OS, na ordem da menor sequência em que cada um aparece (empate pela
    /// letra); o nome é o mais usado para o código (empate pela ordem alfabética).
    /// </summary>
    public static IReadOnlyList<(string Code, string Name)> StepColumns(IEnumerable<IReadOnlyList<BoardStep>> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        return orders.SelectMany(s => s)
            .GroupBy(Key, StringComparer.Ordinal)
            .Select(g => (Code: g.Key, MinSequence: g.Min(s => s.Sequence),
                Name: g.GroupBy(s => s.Name, StringComparer.Ordinal).OrderByDescending(n => n.Count()).ThenBy(n => n.Key, StringComparer.Ordinal).First().Key))
            .OrderBy(c => c.MinSequence).ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(c => (c.Code, c.Name))
            .ToList();
    }

    /// <summary>Horas restantes da etapa (CA 1): max(padrão − apontadas, 0).</summary>
    public static decimal Remaining(decimal standard, decimal applied) => Math.Max(standard - applied, 0m);

    private static string Key(BoardStep step) => string.IsNullOrWhiteSpace(step.Code) ? "?" : step.Code.Trim().ToUpperInvariant();
}
