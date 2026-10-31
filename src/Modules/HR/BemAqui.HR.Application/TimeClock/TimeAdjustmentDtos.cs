using System.Text.Json.Serialization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.HR.Domain;

namespace BemAqui.HR.Application.TimeClock;

/// <summary>Motivo da lista única de ausências (RH-01b CA 7): nome, uso (abono, afastamento ou os dois), se é de saúde e se justifica hora.</summary>
public sealed record AbsenceReasonOption(AbsenceReason Code, string Label, AbsenceReasonUse AppliesTo, bool IsHealth, bool Justifies);

/// <summary>
/// Pedido de ajuste (RH-01b CA 2, 5 e 7). Incluir: <c>day</c>, <c>time</c> ("HH:mm", hora local) e <c>entryType</c>. Desconsiderar:
/// <c>entryId</c>. Abono: <c>day</c>, <c>absenceReason</c> e <c>absenceMinutes</c> (vazio = o dia inteiro). Justificativa de 10 a 200
/// caracteres em todos. No autoatendimento, o funcionário vem da sessão: qualquer campo a mais (ex.: <c>employeeId</c>) responde 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TimeAdjustmentInput(
    TimeAdjustmentKind Kind,
    DateOnly? Day = null,
    string? Time = null,
    TimeEntryType? EntryType = null,
    Guid? EntryId = null,
    int? AbsenceMinutes = null,
    AbsenceReason? AbsenceReason = null,
    string? Justification = null);

/// <summary>Uma marcação do "Lançar o dia pela jornada" (CA 3): tipo e hora local "HH:mm".</summary>
public sealed record TimeAdjustmentDayEntry(TimeEntryType Type, string Time);

/// <summary>"Lançar o dia pela jornada" (CA 3): as marcações (editadas no diálogo) e uma justificativa única; um pedido por marcação.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TimeAdjustmentDayInput(DateOnly Day, IReadOnlyList<TimeAdjustmentDayEntry> Entries, string? Justification);

/// <summary>O diálogo do "Lançar o dia pela jornada" vem preenchido com a jornada do funcionário no dia (CA 3, R-PRP-1).</summary>
public sealed record ScheduleDayProposal(DateOnly Day, string Schedule, IReadOnlyList<TimeAdjustmentDayEntry> Entries, int ExistingEntries);

/// <summary>Decisão sobre o pedido: só a versão lida e o comentário (obrigatório para reprovar; motivo opcional ao cancelar).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TimeAdjustmentDecisionInput(uint Version, string? Comment = null);

public sealed record TimeAdjustmentBatchItem(Guid Id, uint Version);

/// <summary>"Aprovar selecionados" (CA 15): cada pedido decidido na sua transação.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TimeAdjustmentBatchInput(IReadOnlyList<TimeAdjustmentBatchItem> Items);

/// <summary>Resultado de um pedido do lote: aprovado, ou o código e o motivo da falha (próprio, já decidido, período fechado).</summary>
public sealed record TimeAdjustmentBatchResultItem(Guid Id, bool Approved, string? Code, string? Message);

public sealed record TimeAdjustmentBatchResult(int Approved, int Failed, IReadOnlyList<TimeAdjustmentBatchResultItem> Items);

/// <summary>
/// Pedido de ajuste visto por quem consulta (CA 5, 9, 10 e 13). No abono, sem <c>hr.view_health</c> (e sem ser o próprio funcionário):
/// <c>Protected</c>, sem o motivo, a justificativa, o comentário nem o arquivo; só "Ausência justificada" e "Documento anexado".
/// </summary>
public sealed record TimeAdjustmentView(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    TimeAdjustmentKind Kind,
    TimeAdjustmentStatus Status,
    string StatusLabel,
    DateOnly Day,
    string Summary,
    DateTimeOffset? At,
    TimeEntryType? EntryType,
    Guid? EntryId,
    int? AbsenceMinutes,
    AbsenceReason? AbsenceReason,
    string? AbsenceReasonLabel,
    string? Justification,
    bool Protected,
    bool HasDocument,
    IReadOnlyList<AttachmentDto> Documents,
    TimeAdjustmentSource Source,
    Guid? RequestedBy,
    string? RequestedByName,
    DateTimeOffset RequestedAt,
    Guid? DecidedBy,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? DecisionComment,
    bool SelfApproved,
    Guid? CreatedEntryId,
    bool CanDecide,
    bool CanCancel,
    uint Version);

/// <summary>Marcação válida do dia no antes e depois (CA 10); <c>Changed</c> marca a incluída ou a desconsiderada.</summary>
public sealed record TimeAdjustmentDayEntryView(Guid? Id, DateTimeOffset At, TimeEntryType Type, string Label, TimeEntrySource Source, bool Changed);

/// <summary>O dia antes e depois do ajuste (CA 10): marcações válidas e horas trabalhadas (pares fechados), mais o abono.</summary>
public sealed record TimeAdjustmentDayImpact(
    IReadOnlyList<TimeAdjustmentDayEntryView> Before, IReadOnlyList<TimeAdjustmentDayEntryView> After, int WorkedMinutesBefore, int WorkedMinutesAfter,
    int? AbsenceMinutesAfter, bool FullDayAbsence);

public sealed record TimeAdjustmentDetail(TimeAdjustmentView Adjustment, TimeAdjustmentDayImpact Impact);

/// <summary>Pedidos da consulta; <c>Truncated</c> quando passou do limite (filtre por situação ou funcionário).</summary>
public sealed record TimeAdjustmentList(IReadOnlyList<TimeAdjustmentView> Items, bool Truncated);
