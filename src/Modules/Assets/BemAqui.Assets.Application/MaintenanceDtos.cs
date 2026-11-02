using System.Text.Json.Serialization;
using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.Finance.Contracts;

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
    IReadOnlyList<string>? Checklist = null)
{
    /// <summary>PAT-03b CA 1: valor combinado com o prestador (só com <c>assets.edit</c>); gera o título previsto.</summary>
    public decimal? ContractValue { get; init; }

    /// <summary>PAT-03b CA 8: horas paradas por dia (vazio = a sugestão do centro).</summary>
    public decimal? HoursPerDay { get; init; }

    /// <summary>PAT-03b CA 8: duração prevista, em horas paradas no total (vazio = um dia de parada).</summary>
    public decimal? ExpectedHours { get; init; }
}

/// <summary>
/// Reagendar e planejar a OS aberta ou em andamento (PAT-03b CA 1, 2 e 8): data agendada, prestador, valor combinado, horas paradas por dia e
/// duração prevista. Tudo é substituído; valor vazio retira o título previsto.
/// </summary>
public sealed record MaintenanceScheduleInput(
    DateOnly? ScheduledOn, Guid? SupplierPartyId = null, decimal? ContractValue = null, decimal? HoursPerDay = null, decimal? ExpectedHours = null, uint? Version = null);

/// <summary>
/// Sugestões da OS (PAT-03b, propositivo): o prestador pelo executor padrão do plano, o valor pelo da última OS do mesmo plano (só com
/// <c>assets.edit</c>), as horas paradas por dia pelo centro (horas do centro ÷ máquinas ativas) e as horas do centro (o teto).
/// </summary>
public sealed record MaintenanceDefaults(
    Guid? SupplierPartyId,
    string? SupplierName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? ContractValue,
    Guid? WorkCenterId,
    string? WorkCenterName,
    decimal? CenterHoursPerDay,
    int ActiveMachines,
    decimal? HoursPerDay,
    decimal? ExpectedHours);

/// <summary>Título do prestador na OS (só com <c>assets.edit</c>): número, situação, valor, vencimento e a marca "Estimado".</summary>
public sealed record MaintenancePayableView(Guid Id, string Number, PayableStatus Status, decimal Amount, DateOnly? DueDate, bool IsEstimate);

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
    uint Version)
{
    /// <summary>PAT-03b CA 8: horas paradas por dia, duração prevista e o período previsto da parada no centro (sem centro, vazios).</summary>
    public decimal? HoursPerDay { get; init; }

    public decimal? ExpectedHours { get; init; }

    public DateOnly? DowntimeFrom { get; init; }

    public DateOnly? DowntimeTo { get; init; }

    /// <summary>Horas do centro por dia (o teto das horas paradas por dia).</summary>
    public decimal? CenterHoursPerDay { get; init; }

    /// <summary>Com prestador, a conclusão confirma o título a pagar (CA 3).</summary>
    public bool RequiresPayableConfirmation { get; init; }

    /// <summary>PAT-03b CA 7: valor combinado, custo total, o título e o vencimento da confirmação só existem no JSON com <c>assets.edit</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? ContractValue { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Cost { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MaintenancePayableView? Payable { get; init; }

    /// <summary>Vencimento real se a OS for concluída hoje (a data + a primeira parcela da condição do prestador), para o diálogo (CA 3).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? CompletionDueOn { get; init; }
}

/// <summary>"Iniciar" (CA 5): a versão lida.</summary>
public sealed record MaintenanceStartInput(uint? Version = null);

/// <summary>
/// "Concluir manutenção" (CA 6): o que foi feito e, na preventiva por horas, a leitura do horímetro na conclusão (opcional). PAT-03b CA 3: com
/// prestador, <c>ConfirmPayable</c> é a confirmação do diálogo e <c>ContractValue</c> o valor final (vazio = o combinado).
/// </summary>
public sealed record MaintenanceCompleteInput(string? WorkDone, decimal? MeterValue = null, uint? Version = null, bool ConfirmPayable = false, decimal? ContractValue = null);

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
