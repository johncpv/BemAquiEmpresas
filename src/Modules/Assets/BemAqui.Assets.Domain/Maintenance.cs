using System.Globalization;
using BemAqui.Assets.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Assets.Domain;

/// <summary>
/// Plano de manutenção preventiva do bem (<c>assets.maintenance_plans</c>, PAT-03a CA 1 e 2): a cada N dias (<c>next_due_at</c> = última + N) ou
/// a cada N horas de uso (<c>next_due_meter</c> = horímetro da última + N, com a data estimada pela média das últimas leituras). O próximo
/// vencimento conta da última execução, que a conclusão da OS preventiva grava (ADR 0038 §1). Inativo não abre OS (bem baixado, CA 12).
/// </summary>
[DocumentName("Plano de manutenção")]
public sealed class MaintenancePlan : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid AssetId { get; init; }

    public string Description { get; set; } = string.Empty;

    public MaintenanceFrequency FrequencyType { get; set; }

    public int FrequencyValue { get; set; }

    /// <summary>Antecedência do aviso, em dias (padrão 7).</summary>
    public int LeadDays { get; set; } = MaintenanceRules.DefaultLeadDays;

    /// <summary>Executor padrão: prestador (terceiro com papel Fornecedor); vazio = equipe interna.</summary>
    public Guid? DefaultSupplierPartyId { get; set; }

    /// <summary>Itens do checklist (texto), copiados para cada OS aberta pelo plano.</summary>
    public List<string> Checklist { get; set; } = [];

    /// <summary>Data da última execução (<c>date</c>).</summary>
    public DateOnly? LastDoneAt { get; set; }

    /// <summary>Horímetro da última execução (<c>numeric(18,4)</c>), no plano por horas.</summary>
    public decimal? LastDoneMeter { get; set; }

    /// <summary>Próximo vencimento (<c>date</c>): no plano por dias, a data; no plano por horas, a estimativa pelas leituras (pode faltar).</summary>
    public DateOnly? NextDueAt { get; set; }

    /// <summary>Horímetro previsto do próximo vencimento (plano por horas).</summary>
    public decimal? NextDueMeter { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    /// <summary>Recalcula o próximo vencimento a partir da última execução (CA 1, 2 e 6).</summary>
    public void Recalculate(IReadOnlyList<MeterPoint> readings)
    {
        if (FrequencyType == MaintenanceFrequency.Days)
        {
            NextDueMeter = null;
            NextDueAt = LastDoneAt is { } last ? MaintenanceRules.NextDueDate(last, FrequencyValue) : null;
            return;
        }

        NextDueMeter = LastDoneMeter is { } meter ? MaintenanceRules.NextDueMeter(meter, FrequencyValue) : null;
        NextDueAt = NextDueMeter is { } target ? MaintenanceRules.EstimateDate(readings, target) : null;
    }
}

/// <summary>Leitura do horímetro para as contas do plano: data e valor.</summary>
public sealed record MeterPoint(DateOnly ReadOn, decimal Value);

/// <summary>
/// Leitura do horímetro do bem (<c>assets.asset_meter_readings</c>, PAT-03a CA 2): digitada, sempre maior que a anterior. Imutável: só inclusão.
/// </summary>
[DocumentName("Leitura do horímetro")]
public sealed class AssetMeterReading : Entity, IAuditable
{
    public Guid AssetId { get; init; }

    public DateOnly ReadOn { get; init; }

    public decimal Value { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// OS de manutenção (<c>assets.maintenance_orders</c>, PAT-03a, ADR 0038 §1): <c>MAN-AAAA-0001</c>, dona o Assets. <c>Open → InProgress → Done</c>;
/// <c>Open|InProgress → Cancelled</c>. O problema, o que foi feito e o motivo do cancelamento são texto livre e podem citar pessoas:
/// <c>[PersonalData]</c>. A OS aberta pelo job do plano guarda a chave do vencimento (<c>due_key</c>, única por plano: uma OS por vencimento).
/// </summary>
[DocumentName("OS de manutenção")]
public sealed class MaintenanceOrder : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; init; } = string.Empty;

    public Guid AssetId { get; init; }

    public Guid? PlanId { get; init; }

    /// <summary>Vencimento do plano que abriu a OS (job, CA 3); nulo na OS aberta à mão.</summary>
    public string? DueKey { get; init; }

    public MaintenanceOrderType Type { get; init; }

    public MaintenanceOrderStatus Status { get; private set; } = MaintenanceOrderStatus.Open;

    [PersonalData]
    public string Problem { get; set; } = string.Empty;

    /// <summary>Máquina parada: o bem fica "Em manutenção" já na abertura (CA 4).</summary>
    public bool MachineStopped { get; set; }

    /// <summary>Data agendada (<c>date</c>).</summary>
    public DateOnly ScheduledOn { get; set; }

    /// <summary>Prestador (terceiro com papel Fornecedor); vazio = execução interna.</summary>
    public Guid? SupplierPartyId { get; set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public Guid? StartedBy { get; private set; }

    public DateTimeOffset? DoneAt { get; private set; }

    public Guid? DoneBy { get; private set; }

    [PersonalData]
    public string? WorkDone { get; private set; }

    [PersonalData]
    public string? CancelReason { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    /// <summary>"Iniciar" (CA 5): <c>Open → InProgress</c>, com a data e a hora de início.</summary>
    public void Start(Guid? by, DateTimeOffset at)
    {
        if (Status != MaintenanceOrderStatus.Open)
        {
            throw new InvalidOperationException("Só a OS aberta é iniciada.");
        }

        Status = MaintenanceOrderStatus.InProgress;
        StartedAt = at;
        StartedBy = by;
    }

    /// <summary>"Concluir manutenção" (CA 6): <c>InProgress → Done</c>, com o que foi feito.</summary>
    public void Complete(string workDone, Guid? by, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDone);
        if (Status != MaintenanceOrderStatus.InProgress)
        {
            throw new InvalidOperationException("Só a OS em andamento é concluída.");
        }

        Status = MaintenanceOrderStatus.Done;
        WorkDone = workDone;
        DoneAt = at;
        DoneBy = by;
    }

    /// <summary>"Cancelar OS" (CA 7): <c>Open|InProgress → Cancelled</c>, com o motivo.</summary>
    public void Cancel(string reason, Guid? by, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (!MaintenanceRules.IsOpen(Status))
        {
            throw new InvalidOperationException("Só a OS aberta ou em andamento é cancelada.");
        }

        Status = MaintenanceOrderStatus.Cancelled;
        CancelReason = reason;
        CancelledAt = at;
        CancelledBy = by;
    }
}

/// <summary>Item do checklist da OS de manutenção (<c>assets.maintenance_order_checklist</c>), linha a linha, com o resultado.</summary>
public sealed class MaintenanceOrderChecklistItem : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    public int Sequence { get; init; }

    public string Text { get; init; } = string.Empty;

    public MaintenanceChecklistResult? Result { get; set; }

    public DateTimeOffset? MarkedAt { get; set; }

    public Guid? MarkedBy { get; set; }
}

/// <summary>Regras puras da manutenção (PAT-03a).</summary>
public static class MaintenanceRules
{
    public const int DescriptionMax = 150;
    public const int ProblemMax = 1000;
    public const int WorkDoneMax = 2000;
    public const int ReasonMax = 200;
    public const int ChecklistMax = 40;
    public const int ChecklistItemMax = 200;
    public const int DefaultLeadDays = 7;
    public const int LeadDaysMax = 90;
    public const int DaysMax = 3650;
    public const int HoursMax = 100_000;

    /// <summary>Quantas leituras entram na média do uso (CA 2).</summary>
    public const int ReadingsForAverage = 5;

    /// <summary>Janela das preventivas "a vencer" na lista do Patrimônio → Manutenção (CA 10).</summary>
    public const int DueSoonDays = 30;

    /// <summary>Próxima data do plano por dias: a última execução + N dias.</summary>
    public static DateOnly NextDueDate(DateOnly lastDone, int days) => lastDone.AddDays(days);

    /// <summary>Próximo horímetro do plano por horas: o da última execução + N horas.</summary>
    public static decimal NextDueMeter(decimal lastMeter, int hours) => lastMeter + hours;

    /// <summary>
    /// CA 2: data estimada em que o horímetro chega ao previsto, pela média de uso por dia das últimas leituras (até
    /// <see cref="ReadingsForAverage"/>). Sem duas leituras em dias diferentes ou sem uso, não há estimativa. Já alcançado: a data da última leitura.
    /// </summary>
    public static DateOnly? EstimateDate(IReadOnlyList<MeterPoint> readings, decimal target)
    {
        ArgumentNullException.ThrowIfNull(readings);
        var recent = readings.OrderBy(r => r.ReadOn).ThenBy(r => r.Value).TakeLast(ReadingsForAverage).ToList();
        if (recent.Count == 0)
        {
            return null;
        }

        var last = recent[^1];
        if (last.Value >= target)
        {
            return last.ReadOn;
        }

        var first = recent[0];
        var days = last.ReadOn.DayNumber - first.ReadOn.DayNumber;
        var used = last.Value - first.Value;
        if (recent.Count < 2 || days <= 0 || used <= 0)
        {
            return null;
        }

        var perDay = used / days;
        var remaining = (int)Math.Ceiling((target - last.Value) / perDay);
        return remaining > DaysMax * 10 ? null : last.ReadOn.AddDays(remaining);
    }

    /// <summary>
    /// O plano venceu (CA 3, o job abre a OS): por dias, a data chegou; por horas, a última leitura alcançou o horímetro previsto (a data estimada
    /// não vence o plano).
    /// </summary>
    public static bool IsDue(MaintenanceFrequency type, DateOnly? nextDueAt, decimal? nextDueMeter, decimal? currentMeter, DateOnly today) =>
        type == MaintenanceFrequency.Days
            ? nextDueAt is { } date && date <= today
            : nextDueMeter is { } target && currentMeter is { } meter && meter >= target;

    /// <summary>"Vencida" (CA 9, chip crítico): por dias, a data passou; por horas, o horímetro alcançou o previsto.</summary>
    public static bool IsOverdue(MaintenanceFrequency type, DateOnly? nextDueAt, decimal? nextDueMeter, decimal? currentMeter, DateOnly today) =>
        type == MaintenanceFrequency.Days
            ? nextDueAt is { } date && date < today
            : IsDue(type, nextDueAt, nextDueMeter, currentMeter, today);

    /// <summary>Na antecedência do aviso (CA 3): faltam até <paramref name="leadDays"/> dias para a data (prevista ou estimada).</summary>
    public static bool InLead(DateOnly? nextDueAt, int leadDays, DateOnly today) =>
        nextDueAt is { } date && today >= date.AddDays(-leadDays);

    /// <summary>Chave do vencimento (uma OS por vencimento, CA 3): a data no plano por dias, o horímetro previsto no plano por horas.</summary>
    public static string? DueKey(MaintenanceFrequency type, DateOnly? nextDueAt, decimal? nextDueMeter) => type == MaintenanceFrequency.Days
        ? nextDueAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) is { } d ? "d:" + d : null
        : nextDueMeter?.ToString("0.####", CultureInfo.InvariantCulture) is { } m ? "h:" + m : null;

    /// <summary>A OS ainda está aberta (ou em andamento): impede a baixa do bem (CA 12).</summary>
    public static bool IsOpen(MaintenanceOrderStatus status) => status is MaintenanceOrderStatus.Open or MaintenanceOrderStatus.InProgress;

    /// <summary>A OS segura o bem "Em manutenção": em andamento, ou aberta com a máquina parada (CA 4 a 7).</summary>
    public static bool HoldsAsset(MaintenanceOrderStatus status, bool machineStopped) =>
        status == MaintenanceOrderStatus.InProgress || (status == MaintenanceOrderStatus.Open && machineStopped);

    /// <summary>Situação do bem depois da mudança numa OS: em manutenção se alguma OS o segura; senão volta a ativo (o baixado não muda).</summary>
    public static AssetStatus AssetStatusAfter(AssetStatus current, bool anyHolding) =>
        current == AssetStatus.Disposed ? current : anyHolding ? AssetStatus.Maintenance : AssetStatus.Active;

    /// <summary>Itens do checklist que faltam marcar para concluir (CA 6).</summary>
    public static int Missing(IEnumerable<MaintenanceChecklistResult?> results) => results.Count(r => r is null);

    /// <summary>Mensagem do botão desabilitado (CA 6).</summary>
    public static string MissingMessage(int missing) => missing == 1 ? "Marque o item que falta" : $"Marque os {missing} itens que faltam";

    /// <summary>Horímetro: de zero para cima, até 4 casas, cabe em <c>numeric(18,4)</c>.</summary>
    public static bool IsValidMeter(decimal value) => value >= 0 && decimal.Round(value, 4) == value && value < 100_000_000_000_000m;

    /// <summary>Checklist limpo: sem itens vazios nem espaços nas pontas.</summary>
    public static List<string> CleanChecklist(IEnumerable<string?>? items) =>
        (items ?? []).Select(i => i?.Trim()).Where(i => !string.IsNullOrEmpty(i)).Select(i => i!).ToList();

    /// <summary>Texto do horímetro (CA 2): até 4 casas, sem zeros à direita, no formato brasileiro.</summary>
    public static string MeterText(decimal value) => value.ToString("#,##0.####", CultureInfo.GetCultureInfo("pt-BR"));
}
