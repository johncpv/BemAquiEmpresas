using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Production.Domain;

/// <summary>
/// Posição de um projeto na fila de prioridade (<c>production.planning_queue</c>, PLN-02, ADR 0029 §3): um projeto por linha
/// (<c>project_id</c> único), posições contíguas 1..n. Auditada (a fila é decisão de alguém): o histórico do registro guarda a posição
/// antes e depois. Só o <c>PlanningQueueStore</c> e o <c>PlanningQueueService</c> gravam.
/// </summary>
public sealed class PlanningQueueEntry : Entity, IAuditable
{
    public Guid ProjectId { get; init; }

    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Versão da fila (<c>production.planning_queue_state</c>, uma linha por tenant): o <c>xmin</c> sobe a cada "Salvar ordem" e duas
/// gravações sobre a mesma versão dão 409. A entrada e a saída automáticas não mexem na versão (a lista enviada deixa de ser uma
/// permutação e o salvar responde 422). Auditada.
/// </summary>
[DocumentName("A fila")]
public sealed class PlanningQueueState : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid? ReorderedBy { get; set; }

    public DateTimeOffset? ReorderedAt { get; set; }

    public uint Version { get; set; }
}

/// <summary>Tipo da mudança no histórico da fila (PLN-02 CA 2 e 8).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PlanningQueueChangeKind>))]
public enum PlanningQueueChangeKind
{
    /// <summary>A fila foi montada pela data prometida (carga inicial, CA 3).</summary>
    Initial,

    /// <summary>"Salvar ordem" de alguém com <c>production.edit</c> (CA 6).</summary>
    Manual,

    /// <summary>O projeto ganhou a primeira OS de fabricação e entrou pela data prometida (CA 2).</summary>
    Entered,

    /// <summary>As OS de fabricação do projeto terminaram ou foram canceladas (CA 2).</summary>
    Left,
}

/// <summary>
/// Mudança na fila (<c>production.planning_queue_changes</c>, PLN-02 CA 8): quem (nulo = Sistema), quando, o tipo, o motivo e as
/// posições antes → depois (JSON, só para exibir; nunca filtrado no banco). Só inserção.
/// </summary>
public sealed class PlanningQueueChange : Entity
{
    public DateTimeOffset At { get; init; }

    public Guid? UserId { get; init; }

    public PlanningQueueChangeKind Kind { get; init; }

    /// <summary>Projeto que entrou ou saiu; nulo na mudança manual e na carga inicial.</summary>
    public Guid? ProjectId { get; init; }

    public required string Reason { get; init; }

    /// <summary>JSON <c>[{"ProjectId":…,"From":1,"To":3}]</c>; <c>From</c> nulo = entrou, <c>To</c> nulo = saiu.</summary>
    public required string Moves { get; init; }
}
