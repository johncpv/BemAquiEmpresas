using System.Text.Json.Serialization;

namespace BemAqui.Assets.Contracts;

/// <summary>Tipo do bem (R-PAT-1): máquina, ferramenta, mobiliário, veículo e TI.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetType>))]
public enum AssetType
{
    Machine,
    Tool,
    Furniture,
    Vehicle,
    It,
}

/// <summary>Situação do bem: <c>Active</c> na criação, <c>Maintenance</c> pela OS de manutenção (PAT-03a), <c>Disposed</c> pela baixa (PAT-02).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetStatus>))]
public enum AssetStatus
{
    Active,
    Maintenance,
    Disposed,
}

/// <summary>Bem visto pela Produção e pelo Planejamento (PAT-01a CA 15): sem valor de aquisição nem responsável.</summary>
public sealed record AssetInfo(Guid Id, string Tag, string Name, AssetType Type, Guid? WorkCenterId, AssetStatus Status);

/// <summary>Bens do patrimônio para os outros módulos (ADR 0006, ADR 0038): só a identificação, o centro e a situação.</summary>
public interface IAssets
{
    /// <summary>Por id, inclusive baixados; os excluídos não voltam.</summary>
    Task<IReadOnlyList<AssetInfo>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Bens não excluídos nem baixados de um centro de trabalho (as máquinas da capacidade, PAT-03b; o baixado sai, PAT-02).</summary>
    Task<IReadOnlyList<AssetInfo>> ByWorkCenterAsync(Guid workCenterId, CancellationToken cancellationToken = default);
}

/// <summary>Tipo do movimento do bem (PAT-02): transferência (local, responsável, centro), baixa e reativação.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetMovementKind>))]
public enum AssetMovementKind
{
    Transfer,
    Disposal,
    Reactivation,
}

/// <summary>Motivo da baixa (PAT-02 CA 6): venda, descarte, perda ou roubo, doação e outro.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AssetDisposalReason>))]
public enum AssetDisposalReason
{
    Sale,
    Scrap,
    Loss,
    Donation,
    Other,
}

/// <summary>
/// O bem foi baixado (PAT-02): a Produção e o Planejamento deixam de considerar a máquina pelo <see cref="IAssets"/>. Só ids e a data,
/// sem texto livre (ADR 0037 §2); sem consumidor obrigatório nesta story.
/// </summary>
public sealed record AssetDisposed(Guid AssetId, DateOnly DisposedOn) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>Periodicidade do plano preventivo (PAT-03a CA 1): a cada N dias ou a cada N horas de uso (horímetro digitado).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaintenanceFrequency>))]
public enum MaintenanceFrequency
{
    Days,
    Hours,
}

/// <summary>Tipo da OS de manutenção (PAT-03a CA 4): preventiva (do plano ou avulsa) e corretiva.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaintenanceOrderType>))]
public enum MaintenanceOrderType
{
    Preventive,
    Corrective,
}

/// <summary>Situação da OS de manutenção (PAT-03a, <c>04-fluxos</c>): <c>Open → InProgress → Done</c>; <c>Open|InProgress → Cancelled</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaintenanceOrderStatus>))]
public enum MaintenanceOrderStatus
{
    Open,
    InProgress,
    Done,
    Cancelled,
}

/// <summary>Resultado do item do checklist da OS de manutenção (CA 6): feito ou não se aplica.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaintenanceChecklistResult>))]
public enum MaintenanceChecklistResult
{
    Done,
    NotApplicable,
}

/// <summary>
/// OS de manutenção aberta (PAT-03a): só ids, o centro, a marca de máquina parada e a data agendada, sem texto livre (ADR 0037 §2). A parada na
/// capacidade (<c>MaintenanceScheduled</c>) fica para a PAT-03b; sem consumidor nesta story.
/// </summary>
public sealed record MaintenanceOrderOpened(Guid OrderId, Guid AssetId, Guid? WorkCenterId, bool MachineStopped, DateOnly ScheduledOn) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>OS de manutenção concluída (PAT-03a): os mesmos campos da abertura, sem texto livre; sem consumidor nesta story.</summary>
public sealed record MaintenanceOrderCompleted(Guid OrderId, Guid AssetId, Guid? WorkCenterId, bool MachineStopped, DateOnly ScheduledOn) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>
/// Parada da máquina agendada ou mudada (PAT-03b CA 8 e 9, ADR 0038 §4): a OS de manutenção de um bem com centro de trabalho reduz a
/// capacidade do centro de <c>From</c> a <c>To</c> (datas locais, inclusive) em <c>HoursPerDay</c>. <c>OpenEnded</c>: máquina parada, a
/// parada só termina na conclusão. <c>Revision</c> sobe a cada mudança da parada na OS. Só ids, datas e horas, sem texto livre (ADR 0037 §2).
/// O consumidor do Production reconcilia pela fonte (<see cref="IMaintenanceDowntimes"/>), nunca pelo delta do evento.
/// </summary>
public sealed record MaintenanceScheduled(Guid OrderId, Guid AssetId, Guid WorkCenterId, DateOnly From, DateOnly To, decimal HoursPerDay, bool OpenEnded, int Revision)
    : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>OS de manutenção cancelada (PAT-03b CA 8): a parada sai se não começou, ou termina em <c>EndedOn</c> se já começou.</summary>
public sealed record MaintenanceUnscheduled(Guid OrderId, Guid AssetId, Guid WorkCenterId, DateOnly EndedOn, int Revision) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>OS de manutenção concluída (PAT-03b CA 8 e 9): a parada termina em <c>EndedOn</c>, a data da conclusão.</summary>
public sealed record MaintenanceCompleted(Guid OrderId, Guid AssetId, Guid WorkCenterId, DateOnly EndedOn, int Revision) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>
/// Estado atual da parada de uma OS de manutenção (PAT-03b CA 11), lido pelo consumidor do Production: <c>Removed</c> = a parada não
/// existe mais (cancelada antes de começar, bem sem centro); <c>EndedOn</c> = terminou (conclusão ou cancelamento depois do início).
/// </summary>
public sealed record MaintenanceDowntimeState(
    Guid OrderId, string Number, Guid AssetId, Guid? WorkCenterId, DateOnly From, DateOnly To, decimal HoursPerDay, bool OpenEnded, DateOnly? EndedOn, bool Removed, int Revision);

/// <summary>Fonte das paradas por manutenção para a Produção (PAT-03b, ADR 0038 §4): só datas, horas e ids, sem texto nem valores.</summary>
public interface IMaintenanceDowntimes
{
    /// <summary>A parada da OS hoje; null se a OS não existe.</summary>
    Task<MaintenanceDowntimeState?> FindAsync(Guid orderId, CancellationToken cancellationToken = default);

    /// <summary>Várias de uma vez (a tela do centro mostra o número da OS de cada parada).</summary>
    Task<IReadOnlyList<MaintenanceDowntimeState>> FindManyAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);
}

/// <summary>Natureza do custo da manutenção (PAT-03b CA 6): mão de obra interna, prestador (do título), peças e outros.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MaintenanceCostType>))]
public enum MaintenanceCostType
{
    Labor,
    Service,
    Parts,
    Other,
}
