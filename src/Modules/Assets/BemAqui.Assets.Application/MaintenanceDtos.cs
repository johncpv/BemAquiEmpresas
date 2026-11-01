using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Attachments;

namespace BemAqui.Assets.Application;

/// <summary>
/// Plano preventivo (CA 1): descrição, periodicidade (dias ou horas de uso), antecedência do aviso (padrão 7), executor padrão (prestador; vazio =
/// interno), checklist e a última execução (data no plano por dias, horímetro no plano por horas). <c>IsActive</c> e <c>Version</c> só na edição.
/// </summary>
public sealed record MaintenancePlanInput(
    string? Description,
    MaintenanceFrequency? FrequencyType,
    int? FrequencyValue,
    int? LeadDays = null,
    Guid? DefaultSupplierPartyId = null,
    IReadOnlyList<string>? Checklist = null,
    DateOnly? LastDoneAt = null,
    decimal? LastDoneMeter = null,
    bool? IsActive = null,
    uint? Version = null);

/// <summary>
/// Plano na aba Manutenção (CA 9): o próximo vencimento (data, ou horímetro com a data estimada), "Vencida" (<c>Overdue</c>), "A vencer" na
/// antecedência (<c>DueSoon</c>), o horímetro atual e a OS aberta do plano, se houver.
/// </summary>
public sealed record MaintenancePlanView(
    Guid Id,
    Guid AssetId,
    string Description,
    MaintenanceFrequency FrequencyType,
    int FrequencyValue,
    int LeadDays,
    Guid? DefaultSupplierPartyId,
    string? DefaultSupplierName,
    IReadOnlyList<string> Checklist,
    DateOnly? LastDoneAt,
    decimal? LastDoneMeter,
    DateOnly? NextDueAt,
    decimal? NextDueMeter,
    decimal? CurrentMeter,
    bool IsActive,
    bool Overdue,
    bool DueSoon,
    Guid? OpenOrderId,
    string? OpenOrderNumber,
    uint Version);

/// <summary>"Registrar horímetro" (CA 2): valor e data (padrão hoje, até hoje).</summary>
public sealed record MeterReadingInput(decimal? Value, DateOnly? ReadOn = null);

public sealed record MeterReadingView(Guid Id, DateOnly ReadOn, decimal Value, string? CreatedByName, DateTimeOffset CreatedAt);

/// <summary>A leitura gravada e os planos por horas do bem com a previsão nova.</summary>
public sealed record MeterReadingResult(MeterReadingView Reading, IReadOnlyList<MaintenancePlanView> Plans);

/// <summary>Aba Manutenção do bem (CA 9): planos, OS da mais recente à mais antiga e a última leitura do horímetro.</summary>
public sealed record AssetMaintenanceView(IReadOnlyList<MaintenancePlanView> Plans, IReadOnlyList<MaintenanceOrderListItem> Orders, MeterReadingView? LastReading);

/// <summary>
/// "Nova OS de manutenção" (CA 4): o bem, o tipo (Corretiva ou Preventiva avulsa; com <c>PlanId</c>, a preventiva do plano), o problema, se a
/// máquina está parada, a data agendada (padrão hoje), o executor (prestador; vazio = interno) e o checklist (o do plano, quando houver).
/// </summary>
public sealed record MaintenanceOrderInput(
    Guid? AssetId,
    MaintenanceOrderType? Type,
    string? Problem,
    bool MachineStopped = false,
    DateOnly? ScheduledOn = null,
    Guid? SupplierPartyId = null,
    Guid? PlanId = null,
    IReadOnlyList<string>? Checklist = null);

/// <summary>OS na lista e na aba do bem (CA 9 e 10).</summary>
public sealed record MaintenanceOrderListItem(
    Guid Id,
    string Number,
    Guid AssetId,
    string AssetTag,
    string AssetName,
    Guid? WorkCenterId,
    string? WorkCenterName,
    MaintenanceOrderType Type,
    MaintenanceOrderStatus Status,
    bool MachineStopped,
    DateOnly ScheduledOn,
    DateTimeOffset? StartedAt,
    DateTimeOffset? DoneAt,
    DateTimeOffset? CancelledAt,
    Guid? SupplierPartyId,
    string? SupplierName,
    Guid? PlanId,
    string? PlanDescription,
    DateTimeOffset CreatedAt);

public sealed record MaintenanceChecklistItemView(Guid Id, int Sequence, string Text, MaintenanceChecklistResult? Result, DateTimeOffset? MarkedAt, string? MarkedByName);

/// <summary>OS de manutenção (abas Resumo e Checklist): <c>Missing</c> = itens que faltam para concluir; <c>Version</c> = <c>xmin</c> (CA 8).</summary>
public sealed record MaintenanceOrderDetail(
    Guid Id,
    string Number,
    Guid AssetId,
    string AssetTag,
    string AssetName,
    AssetStatus AssetStatus,
    Guid? WorkCenterId,
    string? WorkCenterName,
    MaintenanceOrderType Type,
    MaintenanceOrderStatus Status,
    string Problem,
    bool MachineStopped,
    DateOnly ScheduledOn,
    Guid? SupplierPartyId,
    string? SupplierName,
    Guid? PlanId,
    string? PlanDescription,
    DateTimeOffset? StartedAt,
    string? StartedByName,
    DateTimeOffset? DoneAt,
    string? DoneByName,
    string? WorkDone,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MaintenanceChecklistItemView> Checklist,
    int Missing,
    bool MeterPlan,
    uint Version);

/// <summary>"Iniciar" (CA 5): a versão lida.</summary>
public sealed record MaintenanceStartInput(uint? Version = null);

/// <summary>"Concluir manutenção" (CA 6): o que foi feito e, na preventiva por horas, a leitura do horímetro na conclusão (opcional).</summary>
public sealed record MaintenanceCompleteInput(string? WorkDone, decimal? MeterValue = null, uint? Version = null);

/// <summary>"Cancelar OS" (CA 7): o motivo, até 200 caracteres.</summary>
public sealed record MaintenanceCancelInput(string? Reason, uint? Version = null);

/// <summary>Marcar o item do checklist (CA 6): feito, não se aplica ou vazio (desmarcar).</summary>
public sealed record MaintenanceChecklistMarkInput(MaintenanceChecklistResult? Result);

/// <summary>Filtros de Patrimônio → Manutenção (CA 10), na URL: bem, centro, tipo e situação. Sem situação: as abertas e em andamento.</summary>
public sealed record MaintenanceListQuery(Guid? AssetId, Guid? WorkCenterId, MaintenanceOrderType? Type, MaintenanceOrderStatus? Status);

/// <summary>Preventiva vencida ou a vencer nos próximos 30 dias (CA 10), com a OS aberta do plano, se houver.</summary>
public sealed record MaintenanceDuePlan(
    Guid Id,
    Guid AssetId,
    string AssetTag,
    string AssetName,
    Guid? WorkCenterId,
    string? WorkCenterName,
    string Description,
    MaintenanceFrequency FrequencyType,
    DateOnly? NextDueAt,
    decimal? NextDueMeter,
    decimal? CurrentMeter,
    bool Overdue,
    Guid? OpenOrderId,
    string? OpenOrderNumber);

/// <summary>Patrimônio → Manutenção (CA 10): as OS do filtro e as preventivas vencidas e a vencer.</summary>
public sealed record MaintenanceOverview(IReadOnlyList<MaintenanceOrderListItem> Orders, IReadOnlyList<MaintenanceDuePlan> DuePlans);

/// <summary>Documentos da OS (fotos e relatório do prestador, CA 6).</summary>
public sealed record MaintenanceOrderFiles(IReadOnlyList<AttachmentDto> Files);
