using BemAqui.HR.Domain;

namespace BemAqui.HR.Application.TimeClock;

/// <summary>Marcação válida no dia do espelho: hora local ("08:02"), tipo e origem; nunca a localização.</summary>
public sealed record TimesheetEntryView(Guid Id, string Time, TimeEntryType Type, string TypeLabel, TimeEntrySource Source);

/// <summary>Abono aprovado do dia: o motivo só com <c>hr.view_health</c> ou para o próprio funcionário; sem, "Ausência justificada".</summary>
public sealed record TimesheetAbsenceView(int? Minutes, AbsenceReason? Reason, string Label, bool Protected);

/// <summary>Um dia do espelho (CA 3, 6, 7 e 10). <c>LeaveLabel</c>: "Afastado", com o tipo só com <c>hr.view_health</c>.</summary>
public sealed record TimesheetDayView(
    DateOnly Day,
    TimesheetDayStatus Status,
    string StatusLabel,
    int ExpectedMinutes,
    int WorkedMinutes,
    int OvertimeMinutes,
    int LateMinutes,
    int AbsenceMinutes,
    int JustifiedMinutes,
    int CompensatedMinutes,
    int? BankMinutes,
    IReadOnlyList<string> Issues,
    bool Reviewed,
    string? ReviewComment,
    IReadOnlyList<TimesheetEntryView> Entries,
    IReadOnlyList<TimesheetAbsenceView> Absences,
    string? Holiday,
    string? LeaveLabel);

/// <summary>Totais do período (CA 4, 9 e 10); <c>BankText</c>: "Banco de horas: +3 h 20 min no período, +5 h 10 min acumulado".</summary>
public sealed record TimesheetTotalsView(
    int ExpectedMinutes,
    int WorkedMinutes,
    int OvertimeMinutes,
    int AbsenceMinutes,
    int LateMinutes,
    int JustifiedMinutes,
    int CompensatedMinutes,
    int WorkedDays,
    int AbsenceDays,
    int InconsistentDays,
    bool BankApplies,
    int? BankMonthMinutes,
    int? BankBalanceMinutes,
    string? BankText);

/// <summary>
/// Espelho do funcionário no período (CA 3): a faixa de controle interno, a jornada, os dias, os totais e, fechado, quem e quando ("Período
/// fechado em {data} por {usuário}", CA 12). Período fechado: os dias e os totais são os congelados no fechamento.
/// </summary>
public sealed record TimesheetDetail(
    Guid EmployeeId,
    string EmployeeName,
    string? Registration,
    string PositionName,
    DateOnly Period,
    DateOnly Start,
    DateOnly End,
    TimesheetStatus Status,
    DateTimeOffset? ClosedAt,
    string? ClosedByName,
    string? ClosedBanner,
    DateTimeOffset? ReopenedAt,
    string? ReopenReason,
    DateTimeOffset? EmployeeAckAt,
    string? EmployeeAckSummary,
    string Banner,
    string ScheduleText,
    IReadOnlyList<TimesheetDayView> Days,
    TimesheetTotalsView Totals,
    bool HasEntries,
    int PendingAdjustments);

/// <summary>Linha da visão geral (um funcionário): os totais, o banco, a confirmação do funcionário e o chip de situação.</summary>
public sealed record TimesheetOverviewRow(
    Guid EmployeeId,
    string Name,
    string? Registration,
    TimesheetStatus Status,
    int ExpectedMinutes,
    int WorkedMinutes,
    int OvertimeMinutes,
    int AbsenceMinutes,
    int LateMinutes,
    int JustifiedMinutes,
    int? BankMonthMinutes,
    int? BankBalanceMinutes,
    int InconsistentDays,
    int PendingAdjustments,
    DateTimeOffset? EmployeeAckAt,
    string AckLabel,
    string SituationLabel,
    string SituationTone);

/// <summary>Visão geral do período (RH → Ponto → Espelho): um funcionário por linha.</summary>
public sealed record TimesheetOverview(
    DateOnly Period,
    DateOnly Start,
    DateOnly End,
    DateOnly SuggestedPeriod,
    TimesheetStatus Status,
    DateTimeOffset? ClosedAt,
    string? ClosedByName,
    string? ClosedBanner,
    bool Closable,
    string Banner,
    IReadOnlyList<TimesheetOverviewRow> Rows);

/// <summary>Pendência de um funcionário no fechamento (CA 11): pedidos pendentes e os dias inconsistentes ainda não revisados.</summary>
public sealed record TimesheetClosePrecondition(Guid EmployeeId, string Name, int PendingAdjustments, IReadOnlyList<DateOnly> InconsistentDays);

/// <summary>Pré-condições do "Fechar o período {mês}" (CA 11): as gerais (período ainda em curso, anterior aberto) e as por funcionário.</summary>
public sealed record TimesheetCloseCheck(DateOnly Period, DateOnly Start, DateOnly End, bool CanClose, IReadOnlyList<string> Blockers, IReadOnlyList<TimesheetClosePrecondition> Employees, int EmployeeCount);

/// <summary>"Período {mês} fechado" e quantos espelhos foram congelados.</summary>
public sealed record TimesheetCloseResult(DateOnly Period, int Closed, string Message);

public sealed record TimesheetReopenInput(string? Reason);

/// <summary>"Período reaberto": quantos períodos voltaram a aberto (o pedido e os seguintes já fechados, CA 13).</summary>
public sealed record TimesheetReopenResult(IReadOnlyList<DateOnly> Periods, string Message);

/// <summary>"Marcar como revisado" (CA 11): o dia e o comentário obrigatório (5 a 200).</summary>
public sealed record TimesheetDayReviewInput(DateOnly Day, string? Comment);

/// <summary>Período no "Meu espelho" (CA 8): o em aberto e os fechados, com a ciência.</summary>
public sealed record MyTimesheetPeriod(DateOnly Period, DateOnly Start, DateOnly End, TimesheetStatus Status, DateTimeOffset? ClosedAt, DateTimeOffset? EmployeeAckAt);

/// <summary>"Confirmado" (CA 15): quando e o resumo gravado.</summary>
public sealed record TimesheetAckResult(DateOnly Period, DateTimeOffset At, string Summary);

/// <summary>Acordo de banco de horas do funcionário (CA 2), só com <c>hr.view_sensitive</c>.</summary>
public sealed record HourBankAgreementView(Guid EmployeeId, DateOnly? AgreementOn, bool BankEnabled, uint Version);

public sealed record HourBankAgreementInput(DateOnly? AgreementOn, uint? Version);

/// <summary>Referência de uma rubrica "Origem no ponto" para "Usar horas do espelho" (CA 16): "2:00 h".</summary>
public sealed record PayslipTimesheetReference(Guid PayrollItemId, string Code, string Name, PayrollTimesheetSource Source, int Minutes, string Reference);

/// <summary>Totais do espelho fechado ao lado do holerite (CA 16); sem o período fechado, <c>Available</c> falso com a mensagem.</summary>
public sealed record PayslipTimesheetHours(
    bool Available,
    string Message,
    DateOnly Period,
    DateTimeOffset? ClosedAt,
    int OvertimeMinutes,
    int AbsenceMinutes,
    int LateMinutes,
    int? BankMonthMinutes,
    int? BankBalanceMinutes,
    IReadOnlyList<PayslipTimesheetReference> References);

/// <summary>"Baixar espelhos em PDF" (CA 18): na hora (até 20) ou em segundo plano, com o aviso no sino.</summary>
public sealed record TimesheetBatchResult(bool Queued, int Count, Guid? AttachmentId, string FileName, string Message);

public sealed record TimesheetFile(Stream Content, string FileName, string ContentType);
