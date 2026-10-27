using System.Globalization;
using System.Text.Json.Serialization;
using BemAqui.HR.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>
/// Lista única de ausências do HR (RH-01b CA 7, decisão do PO de 21/10/2026): vale para o abono do ponto e para o afastamento (CAD-04c1),
/// que antes tinha tipo próprio. Migração dos valores antigos: Doença → <see cref="MedicalCertificate"/>, Outros →
/// <see cref="JustifiedAbsence"/>; os demais ficam iguais. Gravado como texto (até 20 caracteres).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AbsenceReason>))]
public enum AbsenceReason
{
    MedicalCertificate,
    WorkAccident,
    Maternity,
    Paternity,
    Military,
    AttendanceNote,
    TimeBankOff,
    JustifiedAbsence,
    UnjustifiedAbsence,
}

/// <summary>Para que serve o motivo: só abono do ponto, só afastamento ou os dois.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AbsenceReasonUse>))]
public enum AbsenceReasonUse
{
    Absence,
    Leave,
    Both,
}

/// <summary>Motivo da lista única: nome, uso, se é dado de saúde (só com <c>hr.view_health</c>) e se conta como hora justificada.</summary>
public sealed record AbsenceReasonInfo(AbsenceReason Code, string Label, AbsenceReasonUse AppliesTo, bool IsHealth, bool Justifies);

/// <summary>A lista única de ausências (RH-01b CA 7) e as regras dela.</summary>
public static class AbsenceReasons
{
    /// <summary>
    /// Na ordem da tela. "Falta não justificada" é só registro (não justifica hora); "Folga compensada pelo banco de horas" desconta do
    /// banco no espelho (RH-01c CA 10) e não conta como hora abonada.
    /// </summary>
    public static readonly IReadOnlyList<AbsenceReasonInfo> All =
    [
        new(AbsenceReason.MedicalCertificate, "Atestado médico", AbsenceReasonUse.Both, true, true),
        new(AbsenceReason.WorkAccident, "Acidente de trabalho", AbsenceReasonUse.Both, true, true),
        new(AbsenceReason.Maternity, "Maternidade", AbsenceReasonUse.Leave, false, true),
        new(AbsenceReason.Paternity, "Paternidade", AbsenceReasonUse.Both, false, true),
        new(AbsenceReason.Military, "Serviço militar", AbsenceReasonUse.Both, false, true),
        new(AbsenceReason.AttendanceNote, "Declaração de comparecimento", AbsenceReasonUse.Absence, false, true),
        new(AbsenceReason.TimeBankOff, "Folga compensada pelo banco de horas", AbsenceReasonUse.Absence, false, false),
        new(AbsenceReason.JustifiedAbsence, "Falta justificada", AbsenceReasonUse.Both, false, true),
        new(AbsenceReason.UnjustifiedAbsence, "Falta não justificada", AbsenceReasonUse.Absence, false, false),
    ];

    public static AbsenceReasonInfo Info(AbsenceReason reason) => All.First(r => r.Code == reason);

    public static string Label(AbsenceReason reason) => Info(reason).Label;

    public static bool IsHealth(AbsenceReason? reason) => reason is { } r && Info(r).IsHealth;

    public static bool ForLeave(AbsenceReason reason) => Enum.IsDefined(reason) && Info(reason).AppliesTo is AbsenceReasonUse.Leave or AbsenceReasonUse.Both;

    public static bool ForAbsence(AbsenceReason reason) => Enum.IsDefined(reason) && Info(reason).AppliesTo is AbsenceReasonUse.Absence or AbsenceReasonUse.Both;

    /// <summary>Valor antigo do tipo de afastamento (CAD-04c1) para o motivo da lista única: a regra da migração, sem perder dado.</summary>
    public static AbsenceReason FromLegacyLeaveType(string legacy) => legacy switch
    {
        "Illness" => AbsenceReason.MedicalCertificate,
        "Other" => AbsenceReason.JustifiedAbsence,
        _ => Enum.Parse<AbsenceReason>(legacy),
    };
}

/// <summary>O que o pedido de ajuste muda: incluir uma marcação, desconsiderar uma marcação ou abonar uma ausência.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeAdjustmentKind>))]
public enum TimeAdjustmentKind
{
    AddEntry,
    VoidEntry,
    Absence,
}

/// <summary>Quem fez o pedido: o próprio funcionário ("Meu ponto") ou o RH (lançamento manual).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeAdjustmentSource>))]
public enum TimeAdjustmentSource
{
    Employee,
    HR,
}

[JsonConverter(typeof(JsonStringEnumConverter<TimeAdjustmentStatus>))]
public enum TimeAdjustmentStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>
/// Pedido de ajuste do ponto (<c>hr.time_adjustments</c>, RH-01b): incluir marcação esquecida, desconsiderar marcação errada ou abonar uma
/// ausência. Nasce <c>Pending</c> e só a aprovação muda o ponto: cria a marcação <c>Manual</c>, passa a original a <c>Voided</c> (nunca
/// apaga) ou faz o abono valer no espelho (RH-01c). Quem pede não aprova (salvo o Proprietário único, com <see cref="SelfApproved"/>).
/// A justificativa, o motivo da ausência e o comentário da decisão são <c>[PersonalData]</c>: mascarados na auditoria e fora de log,
/// evento e notificação.
/// </summary>
[DocumentName("O ajuste")]
public sealed class TimeAdjustment : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid EmployeeId { get; init; }

    public TimeAdjustmentKind Kind { get; init; }

    /// <summary>Dia (data local) a que o pedido se refere: o da marcação incluída, o da desconsiderada ou o da ausência.</summary>
    public DateOnly Day { get; init; }

    /// <summary>Marcação desconsiderada (só no <see cref="TimeAdjustmentKind.VoidEntry"/>).</summary>
    public Guid? EntryId { get; init; }

    /// <summary>Instante da marcação a incluir, em UTC (só no <see cref="TimeAdjustmentKind.AddEntry"/>).</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>Tipo da marcação a incluir (só no <see cref="TimeAdjustmentKind.AddEntry"/>).</summary>
    public TimeEntryType? EntryType { get; init; }

    /// <summary>Minutos abonados; null no abono do dia inteiro (só no <see cref="TimeAdjustmentKind.Absence"/>).</summary>
    public int? AbsenceMinutes { get; init; }

    /// <summary>Motivo da lista única (só no abono): o de saúde só com <c>hr.view_health</c>.</summary>
    [PersonalData]
    public AbsenceReason? AbsenceReason { get; init; }

    /// <summary>Justificativa de 10 a 200 caracteres, sem CID nem diagnóstico.</summary>
    [PersonalData]
    public string Justification { get; init; } = string.Empty;

    public TimeAdjustmentSource Source { get; init; }

    public Guid? RequestedBy { get; init; }

    public DateTimeOffset RequestedAt { get; init; }

    public TimeAdjustmentStatus Status { get; set; } = TimeAdjustmentStatus.Pending;

    public Guid? DecidedBy { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Comentário da decisão (obrigatório na reprovação) ou motivo do cancelamento.</summary>
    [PersonalData]
    public string? DecisionComment { get; set; }

    /// <summary>Aprovado pelo próprio solicitante ou pelo dono das marcações, como Proprietário único (CA 13).</summary>
    public bool SelfApproved { get; set; }

    /// <summary>Marcação criada pela aprovação do <see cref="TimeAdjustmentKind.AddEntry"/>.</summary>
    public Guid? CreatedEntryId { get; set; }

    public uint Version { get; set; }
}

/// <summary>Regras do pedido de ajuste sem banco (RH-01b): datas, justificativa, jornada do dia, textos e segregação.</summary>
public static class TimeAdjustmentRules
{
    public const int JustificationMin = 10;
    public const int JustificationMax = 200;
    public const int CommentMax = 500;

    /// <summary>Abono parcial: de 1 minuto até 24 h.</summary>
    public const int MaxAbsenceMinutes = 24 * 60;

    public const string ApprovePermission = "hr.approve";
    public const string FutureMessage = "Não é possível lançar marcação no futuro.";
    public const string OutOfPeriodMessage = "Data fora do período em aberto.";
    public const string OwnMessage = "Você não pode aprovar um ajuste seu.";
    public const string NoCidHint = "Não informe CID nem diagnóstico.";
    public const string ProtectedLabel = "Ausência justificada";
    public const string DocumentLabel = "Documento anexado";
    public const string SelfApprovedLabel = "autoaprovado";
    public const string VoidHint = "A marcação continua guardada, só deixa de contar.";

    /// <summary>Justificativa obrigatória de 10 a 200 caracteres (CA 2); devolve a mensagem ou null.</summary>
    public static string? JustificationError(string? text)
    {
        var length = (text ?? string.Empty).Trim().Length;
        return length < JustificationMin || length > JustificationMax
            ? $"Escreva a justificativa com {JustificationMin} a {JustificationMax} caracteres. {NoCidHint}"
            : null;
    }

    /// <summary>
    /// Data do pedido (CA 4 e 16): não pode ser no futuro, nem antes do período mais antigo em aberto, nem fora do vínculo do funcionário
    /// (antes da admissão ou depois do desligamento).
    /// </summary>
    public static string? DateError(DateOnly day, DateOnly today, DateOnly? oldestOpen, DateOnly admission, DateOnly? termination)
    {
        if (day > today)
        {
            return FutureMessage;
        }

        if ((oldestOpen is { } open && day < open) || day < admission || (termination is { } end && day > end))
        {
            return OutOfPeriodMessage;
        }

        return null;
    }

    /// <summary>A marcação a incluir não pode ser depois de agora (CA 4).</summary>
    public static bool IsFuture(DateTimeOffset at, DateTimeOffset now) => at > now;

    /// <summary>
    /// "Lançar o dia pela jornada" (CA 3): as marcações da jornada do funcionário no dia da semana: entrada, saída e volta do intervalo e
    /// saída. A jornada guarda só a duração do intervalo; a saída para o intervalo sai às 12:00 quando cabe (entrada antes e a volta antes da
    /// saída) e, senão, na metade do dia. Sem intervalo, só entrada e saída; sem o dia na jornada, nada (o diálogo abre vazio).
    /// </summary>
    public static IReadOnlyList<(TimeEntryType Type, TimeOnly Time)> ScheduleEntries(IReadOnlyList<ScheduleDay>? days, DateOnly day)
    {
        var weekday = day.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)day.DayOfWeek;
        var today = days?.FirstOrDefault(d => d.Weekday == weekday);
        if (today is null || !TimeOnly.TryParseExact(today.Start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(today.End, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end <= start)
        {
            return [];
        }

        if (today.BreakMinutes <= 0)
        {
            return [(TimeEntryType.In, start), (TimeEntryType.Out, end)];
        }

        var noon = new TimeOnly(12, 0);
        var pause = TimeSpan.FromMinutes(today.BreakMinutes);
        var breakStart = start < noon && noon.Add(pause) < end
            ? noon
            : start.Add(TimeSpan.FromMinutes(Math.Floor(((end - start).TotalMinutes - today.BreakMinutes) / 2)));
        return [(TimeEntryType.In, start), (TimeEntryType.BreakStart, breakStart), (TimeEntryType.BreakEnd, breakStart.Add(pause)), (TimeEntryType.Out, end)];
    }

    /// <summary>
    /// O que muda (CA 10): "Incluir saída às 17:50", "Desconsiderar entrada às 07:12", "Abonar 4 h: Atestado médico". Sem
    /// <paramref name="showReason"/>, o abono sai como "Abonar 4 h: Ausência justificada" (CA 9).
    /// </summary>
    public static string Summary(TimeAdjustmentKind kind, TimeEntryType? type, DateTimeOffset? localAt, int? absenceMinutes, AbsenceReason? reason, bool showReason)
    {
        switch (kind)
        {
            case TimeAdjustmentKind.AddEntry:
                return $"Incluir {Lower(TimeClockRules.Label(type ?? TimeEntryType.In))} às {Time(localAt)}";
            case TimeAdjustmentKind.VoidEntry:
                return $"Desconsiderar {Lower(TimeClockRules.Label(type ?? TimeEntryType.In))} às {Time(localAt)}";
            default:
                var what = showReason && reason is { } r ? AbsenceReasons.Label(r) : ProtectedLabel;
                return $"Abonar {Duration(absenceMinutes)}: {what}";
        }
    }

    /// <summary>"o dia" (abono do dia inteiro), "4 h", "1 h 30 min", "45 min".</summary>
    public static string Duration(int? minutes)
    {
        if (minutes is not { } m)
        {
            return "o dia";
        }

        var hours = m / 60;
        var rest = m % 60;
        return hours > 0 && rest == 0 ? $"{hours} h" : hours > 0 ? $"{hours} h {rest} min" : $"{rest} min";
    }

    public static string StatusLabel(TimeAdjustmentStatus status) => status switch
    {
        TimeAdjustmentStatus.Pending => "Aguardando aprovação",
        TimeAdjustmentStatus.Approved => "Aprovado",
        TimeAdjustmentStatus.Rejected => "Reprovado",
        _ => "Cancelado",
    };

    /// <summary>
    /// Segregação (CA 13): o envolvido (quem pediu ou o dono das marcações) só decide quando não há outro aprovador elegível
    /// (Proprietário único, <c>SoleOwnerRule</c>), e aí com a marca "autoaprovado".
    /// </summary>
    public static bool MayDecide(IReadOnlyCollection<Guid> involved, Guid actor, bool noOtherEligible) => !involved.Contains(actor) || noOtherEligible;

    /// <summary>Trabalhado do dia em minutos, pelos pares fechados (entrada ou volta do intervalo seguida de saída ou saída para o intervalo).</summary>
    public static int WorkedMinutes(IEnumerable<(DateTimeOffset At, TimeEntryType Type)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var total = 0d;
        DateTimeOffset? open = null;
        foreach (var (at, type) in entries.OrderBy(e => e.At))
        {
            if (type is TimeEntryType.In or TimeEntryType.BreakEnd)
            {
                open = at;
            }
            else if (open is { } started)
            {
                total += (at - started).TotalMinutes;
                open = null;
            }
        }

        return (int)Math.Round(total, MidpointRounding.AwayFromZero);
    }

    /// <summary>Notificação ao aprovador (CA 17), sem justificativa nem motivo.</summary>
    public static string ApproverNotice(string employeeName) => $"Ajuste de ponto de {employeeName} para aprovar";

    public static string ApprovedNotice(DateOnly day) => $"Seu ajuste de {Date(day)} foi aprovado.";

    public static string RejectedNotice(DateOnly day, string comment) => $"Seu ajuste de {Date(day)} foi reprovado: {comment}";

    /// <summary>CA 19: "Desconsiderada por Ana em 22/10/2026: marcação em dobro".</summary>
    public static string VoidedNote(string? approver, DateTimeOffset localAt, string? reason) =>
        $"Desconsiderada por {approver ?? "aprovador"} em {localAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}" + (string.IsNullOrWhiteSpace(reason) ? string.Empty : ": " + reason);

    public static string DedupKey(Guid id) => "time-adjustment:" + id;

    public static string Date(DateOnly day) => day.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Time(DateTimeOffset? local) => local?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "--:--";

    private static string Lower(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
