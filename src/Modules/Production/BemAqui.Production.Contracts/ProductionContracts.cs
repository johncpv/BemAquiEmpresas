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

/// <summary>Tipo da OS para os outros módulos (PRD-01 CA 21).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductionOrderKind>))]
public enum ProductionOrderKind
{
    Installation,
    Fabrication,
}

/// <summary>Situação da OS de fabricação para os outros módulos (04-fluxos §1).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ProductionOrderState>))]
public enum ProductionOrderState
{
    Open,
    InProgress,
    Paused,
    Quality,
    Completed,
    Cancelled,

    /// <summary>"Pronta para concluir" (PRD-06a1 CA 1).</summary>
    ReadyToComplete,
}

/// <summary>
/// OS de fabricação vista pelos outros módulos (PRD-01 CA 21): número, tipo, projeto, item do projeto, produto, quantidade,
/// situação e etapa atual (nome e letra), sem dado de custo. PRD-06a1 CA 15: a conclusão (quando e quem) e o último estorno da
/// conclusão (quando, quem e o motivo), para a linha do tempo do projeto. ENG-02 CA 17: a sequência da etapa atual (1..n; null sem
/// etapa pendente) e se a primeira etapa já está <c>Done</c>, para a lista "OS que vão parar sem BOM".
/// </summary>
public sealed record ProductionOrderInfo(
    Guid Id, string Number, ProductionOrderKind Type, Guid ProjectId, Guid ProjectItemId, Guid? ProductId, decimal Quantity, ProductionOrderState Status,
    string? CurrentStepName, string? CurrentStepCode, DateTimeOffset? CompletedAt = null, Guid? CompletedBy = null, DateTimeOffset? CompletionReversedAt = null,
    Guid? CompletionReversedBy = null, string? CompletionReversalReason = null, int? CurrentStepSequence = null, bool FirstStepDone = false, int StepCount = 0);

/// <summary>
/// OS de fabricação para Engineering, Inventory, Purchasing e Commercial (PRD-01 CA 21, ADR 0006): só o tipo Fabrication, sobre a
/// mesma tabela da instalação (que tem o próprio <see cref="IInstallationOrders"/>).
/// </summary>
public interface IProductionOrders
{
    /// <summary>Por id; null se não existe ou não é de fabricação.</summary>
    Task<ProductionOrderInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionOrderInfo>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionOrderInfo>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Situação da OS de fabricação; null se não existe ou não é de fabricação.</summary>
    Task<ProductionOrderState?> StatusAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>OS de fabricação em aberto (nem concluídas nem canceladas), para a lista de BOMs da Engenharia (ENG-02 CA 17).</summary>
    Task<IReadOnlyList<ProductionOrderInfo>> OpenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Primeira etapa da OS de fabricação iniciada: a OS saiu de <c>Open</c> (PRD-02 CA 8 pela "Concluir etapa" do supervisor; PRD-03a CA 7
/// pelo apontamento). O Commercial passa o projeto a <c>InProgress</c> se ainda estava <c>Planned</c>.
/// </summary>
public sealed record ProductionStepStarted(Guid ProductionOrderId, Guid ProjectId, Guid StepId, DateTimeOffset StartedAt) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>
/// Qualidade aprovada (PRD-05 CA 3): a etapa de qualidade foi a <c>Done</c> na tentativa dada, com a marca "Autoinspeção" quando
/// quem aprovou fez parte do trabalho. Consumidores futuros: DSH-03 (indicadores) e PNL-01 (painel).
/// </summary>
public sealed record QualityApproved(Guid ProductionOrderId, Guid StepId, int Attempt, bool SelfInspection) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>Qualidade reprovada (PRD-05 CA 6): nasceu a NC e a OS voltou à etapa de retorno.</summary>
public sealed record QualityRejected(Guid NonConformityId, Guid ProductionOrderId, Guid ProjectId, Guid ReturnToStepId) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>
/// OS de fabricação concluída (PRD-06a1 CA 3), com a quantidade inteira (sem conclusão parcial, ponto 82) e a data da conclusão. O
/// Commercial confere se todas as OS de fabricação não canceladas do projeto concluíram e publica <c>ProjectFabricated</c> (CA 7). A
/// entrada do produto acabado no estoque já foi feita de forma síncrona na mesma transação (PRD-06a2 CA 5, ADR 0023 §1); o evento leva o
/// produto, o custo unitário, as séries e o local para a projeção (painéis), com valores padrão para as mensagens antigas do outbox.
/// </summary>
public sealed record ProductionOrderCompleted(
    Guid ProductionOrderId, Guid ProjectId, decimal Quantity, DateOnly CompletedOn, Guid? ProductId = null, decimal UnitCost = 0, IReadOnlyList<string>? SerialNumbers = null,
    Guid? LocationId = null) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>
/// Conclusão da OS de fabricação estornada (PRD-06a1 CA 10): a OS voltou a <c>InProgress</c>. O Commercial volta o projeto de
/// <c>Fabricated</c> a <c>InProgress</c> quando ele foi fabricado pelas OS e avisa o Financeiro; o título a receber não é estornado.
/// </summary>
public sealed record ProductionOrderCompletionReversed(Guid ProductionOrderId, Guid ProjectId, DateOnly ReversedOn) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>OS de fabricação cancelada (PRD-01 CA 16): a EST-02 reconcilia as reservas pela fonte (ADR 0023 §6).</summary>
public sealed record ProductionOrderCancelled(Guid ProductionOrderId, Guid ProjectId) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>Origem do custo do apontamento (PRD-04 CA 17): fechamento, correção, recálculo sem custo-hora ou implantação (D3).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LaborCostOrigin>))]
public enum LaborCostOrigin
{
    Closing,
    Correction,
    Recalculation,
    Backfill,
}

/// <summary>
/// Apontamento fechado (PRD-03a CA 13, 14 e 20; PRD-04 CA 1 e 12): minutos líquidos = (fim − início) − pausas e o custo pelo custo-hora
/// do cargo no fechamento, como razão de diferenças (ADR 0025 §2): <c>Version</c> (versão do custo, 1 no fechamento),
/// <c>PreviousAmount</c> (0) e <c>NewAmount</c> (custo do apontamento). Só ids, valores e data (CA 15). <c>Origin</c> é
/// <c>Closing</c> ou <c>Backfill</c> (recomposição dos apontamentos fechados antes da PRD-04). Mensagem antiga no outbox chega
/// com <c>Version</c> 0 e o consumidor ignora (a recomposição cobre).
/// </summary>
public sealed record LaborEntryClosed(
    Guid LaborEntryId, Guid ProductionOrderId, Guid ProjectId, Guid StepId, Guid EmployeeId, int Minutes, int Version = 0, decimal PreviousAmount = 0, decimal NewAmount = 0,
    DateOnly OccurredOn = default, LaborCostOrigin Origin = LaborCostOrigin.Closing) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>
/// Custo do apontamento refeito (PRD-03a CA 20; PRD-04 CA 4 e 6): correção do supervisor (<c>Correction</c>: mesmo snapshot e as horas
/// novas) ou recálculo do apontamento sem custo-hora (<c>Recalculation</c>), com a versão nova e os valores antes e depois. Mensagem
/// antiga (só o id) chega com <c>Version</c> 0 e o consumidor ignora.
/// </summary>
public sealed record LaborEntryCorrected(
    Guid LaborEntryId, Guid ProductionOrderId = default, Guid ProjectId = default, int Minutes = 0, int Version = 0, decimal PreviousAmount = 0, decimal NewAmount = 0,
    DateOnly OccurredOn = default, LaborCostOrigin Origin = LaborCostOrigin.Correction) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>Situação do romaneio para os outros módulos (PRD-06b): rascunho, aguardando o estoque geral, expedido, cancelado e estornado.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ShipmentState>))]
public enum ShipmentState
{
    Draft,
    AwaitingStockApproval,
    Shipped,
    Cancelled,
    Reversed,
}

/// <summary>Linha do romaneio (PRD-06b CA 12): item, quantidade, local, lote, séries e a saída <c>Shipment</c> no estoque. Sem valor nem custo.</summary>
public sealed record ShipmentLineInfo(
    Guid LineId, int Sequence, Guid ItemId, decimal Quantity, Guid LocationId, Guid? LotId, bool FromGeneralStock, IReadOnlyList<Guid> SerialIds, IReadOnlyList<string> SerialNumbers,
    Guid? MovementId);

/// <summary>
/// Romaneio visto pelos outros módulos (PRD-06b CA 12 e 13): o Commercial mostra no Projeto 360; a NF-e de saída da Fase 5 (FIS-03) e a
/// rastreabilidade (EST-04) leem as linhas e as séries. Sem valor (o romaneio não é documento fiscal) e sem dado pessoal.
/// </summary>
public sealed record ShipmentInfo(
    Guid Id, string Number, Guid ProjectId, Guid PartyId, ShipmentState Status, DateOnly? PlannedDate, DateTimeOffset CreatedAt, DateTimeOffset? ShippedAt, Guid? ShippedBy,
    DateTimeOffset? ReversedAt, Guid? ReversedBy, string? ReversalReason, IReadOnlyList<ShipmentLineInfo> Lines);

/// <summary>
/// Romaneios da Produção para Commercial, Fiscal (Fase 5) e Inventory (EST-04), ADR 0006. A NF-e de saída referencia o romaneio
/// (<c>outgoing_invoices.shipment_id</c>) e nunca baixa o estoque de novo (D4): a baixa já foi feita na confirmação.
/// </summary>
public interface IShipments
{
    /// <summary>Por id, com as linhas e as séries; null se não existe.</summary>
    Task<ShipmentInfo?> ByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Romaneios do projeto (todas as situações), do mais antigo ao mais novo.</summary>
    Task<IReadOnlyList<ShipmentInfo>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
}

/// <summary>Linha do <see cref="ShipmentDispatched"/>: item, séries, quantidade e local da saída.</summary>
public sealed record ShipmentDispatchedLine(Guid ItemId, IReadOnlyList<Guid> SerialIds, decimal Quantity, Guid LocationId);

/// <summary>
/// Romaneio expedido (PRD-06b CA 4): só para avisos e painéis. A saída do produto acabado (<c>Shipment</c>) e as séries expedidas já
/// foram gravadas de forma síncrona na confirmação (D4, ADR 0023 §1); nenhum consumidor mexe no estoque. Sem dado pessoal.
/// </summary>
public sealed record ShipmentDispatched(Guid ShipmentId, Guid ProjectId, IReadOnlyList<ShipmentDispatchedLine> Lines) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>Expedição estornada (PRD-06b CA 11): as entradas inversas e as séries de volta ao estoque já foram gravadas; só aviso e projeção.</summary>
public sealed record ShipmentReversed(Guid ShipmentId, Guid ProjectId, DateOnly ReversedOn) : BemAqui.SharedKernel.IIntegrationEvent;

/// <summary>Apontamento visto pelo drill-down de horas do projeto (DSH-02 CA 14), sem nome nem custo-hora: OS, centro de trabalho e minutos.</summary>
public sealed record LaborEntryCostInfo(Guid LaborEntryId, Guid OrderId, string OrderNumber, Guid? WorkCenterId, string? WorkCenterName, int Minutes);

/// <summary>Quem trabalhou no apontamento e o custo-hora do snapshot (DSH-02 CA 14): só com <c>projects.view_cost</c> e <c>hr.view_sensitive</c>.</summary>
public sealed record LaborEntryWorkerInfo(Guid LaborEntryId, string WorkerName, decimal? HourlyCostSnapshot);

/// <summary>Não conformidades das OS do projeto (DSH-02 CA 12): quantidade e custo estimado, só informativo (fora de qualquer coluna de custo).</summary>
public sealed record ProjectNonConformityCost(int Count, decimal EstimatedCost);

/// <summary>
/// Detalhe do custo de produção do projeto para o Commercial (DSH-02 CA 12 e 14). O valor das horas vem da razão do Commercial; aqui só o que
/// explica a linha (OS, centro, minutos). O chamador confere <c>projects.view_cost</c> e, para <see cref="LaborWorkersAsync"/>, também
/// <c>hr.view_sensitive</c>.
/// </summary>
public interface IProjectProductionCosts
{
    Task<IReadOnlyList<LaborEntryCostInfo>> LaborEntriesAsync(IReadOnlyCollection<Guid> laborEntryIds, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LaborEntryWorkerInfo>> LaborWorkersAsync(IReadOnlyCollection<Guid> laborEntryIds, CancellationToken cancellationToken = default);

    Task<ProjectNonConformityCost> NonConformitiesAsync(Guid projectId, CancellationToken cancellationToken = default);
}
