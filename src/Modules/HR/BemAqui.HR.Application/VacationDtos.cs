using BemAqui.HR.Domain;

namespace BemAqui.HR.Application;

/// <summary>
/// Período aquisitivo na aba "Férias" (FLH-03 CA 1 e 2), sem valor: início, fim, faltas, dias de direito, gozados, vendidos, saldo, fim do
/// concessivo, a situação com o chip ("Em aquisição", "A gozar", "Vence em {data}", "Vencido") e o tom, e a memória (reinício).
/// </summary>
public sealed record VacationPeriodItem(
    Guid Id,
    int Sequence,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int Absences,
    int EntitledDays,
    int TakenDays,
    int SoldDays,
    int Balance,
    DateOnly ConcessionEndsOn,
    VacationPeriodStatus Status,
    string StatusLabel,
    string Tone,
    string? RestartedReason,
    DateOnly? RestartedOn,
    IReadOnlyList<string> Memory);

/// <summary>Programação de férias (CA 4, 5 e 11), sem valor: as datas, os dias, o abono, o vencimento, a situação e a folha de férias.</summary>
public sealed record VacationScheduleItem(
    Guid Id,
    Guid EmployeeId,
    Guid PeriodId,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int Days,
    int SoldDays,
    bool Advance13th,
    DateOnly PaymentDueOn,
    VacationScheduleStatus Status,
    string StatusLabel,
    Guid? PayrollRunId,
    Guid? PayslipId,
    bool Calculated,
    bool RunClosed,
    bool IsSimulation,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    uint Version);

/// <summary>Sugestão do diálogo "Programar férias" (Propositivo): o período, o primeiro início permitido depois do aviso, os dias e o abono máximo.</summary>
public sealed record VacationSuggestionView(Guid PeriodId, DateOnly StartsOn, int Days, int MaxSoldDays);

/// <summary>Os parâmetros que o diálogo mostra (com o chip "A confirmar com a contadora" na tela de Parâmetros).</summary>
public sealed record VacationRulesView(int MaxParts, int FirstPartMinDays, int OtherPartMinDays, int NoticeDays, int PaymentDaysBefore);

/// <summary>Aba "Férias" do funcionário (CA 1, 4 e 11).</summary>
public sealed record EmployeeVacations(Guid EmployeeId, string Name, IReadOnlyList<VacationPeriodItem> Periods, IReadOnlyList<VacationScheduleItem> Schedules, VacationSuggestionView? Suggestion, VacationRulesView Rules);

/// <summary>"Programar férias" (CA 4): o período aquisitivo, o início, os dias de gozo, os dias vendidos e o adiantamento do 13º.</summary>
public sealed record VacationScheduleInput(Guid? PeriodId, DateOnly? StartsOn, int Days, int SoldDays, bool Advance13th);

/// <summary>"Férias programadas" (CA 5): a programação, os avisos (aviso de 30 dias, em dobro) e os próximos passos.</summary>
public sealed record VacationScheduleResult(VacationScheduleItem Schedule, IReadOnlyList<string> Warnings, IReadOnlyList<PayrollNextStep> NextSteps, string Message);

/// <summary>"Cancelar programação" (CA 11): o motivo (5 a 200) e a versão.</summary>
public sealed record VacationCancelInput(string? Reason, uint Version);

/// <summary>"Férias calculadas" (CA 6 e 8): a programação, a folha de férias, a mensagem e os próximos passos.</summary>
public sealed record VacationCalculationResult(VacationScheduleItem Schedule, PayrollRunSummary Run, Guid PayslipId, string Message, IReadOnlyList<PayrollNextStep> NextSteps);

/// <summary>Vencimento na tela RH → Férias (CA 3): o funcionário, o período, o saldo e o fim do concessivo.</summary>
public sealed record VacationDueRow(Guid EmployeeId, string Name, string? Registration, Guid PeriodId, DateOnly PeriodStart, DateOnly PeriodEnd, int Balance, DateOnly ConcessionEndsOn,
    VacationPeriodStatus Status, string StatusLabel, string Tone);

/// <summary>Férias no calendário do mês (RH → Férias).</summary>
public sealed record VacationCalendarEntry(Guid ScheduleId, Guid EmployeeId, string Name, DateOnly StartsOn, DateOnly EndsOn, int Days, VacationScheduleStatus Status, string StatusLabel);

public sealed record VacationCalendarMonth(DateOnly Month, IReadOnlyList<VacationCalendarEntry> Entries);

/// <summary>RH → Férias: os vencimentos dos próximos dias (e os vencidos) e o calendário dos meses.</summary>
public sealed record VacationOverview(int Days, IReadOnlyList<VacationDueRow> Due, IReadOnlyList<VacationCalendarMonth> Calendar);

/// <summary>Período no "Meu ponto" (CA 5 e 12): datas e saldo, sem valor.</summary>
public sealed record MyVacationPeriod(DateOnly StartsOn, DateOnly EndsOn, int EntitledDays, int TakenDays, int SoldDays, int Balance, DateOnly ConcessionEndsOn, VacationPeriodStatus Status, string StatusLabel);

/// <summary>Férias programadas no "Meu ponto": datas, dias e situação, sem valor.</summary>
public sealed record MyVacationSchedule(Guid Id, DateOnly StartsOn, DateOnly EndsOn, int Days, int SoldDays, VacationScheduleStatus Status, string StatusLabel);

public sealed record MyVacations(IReadOnlyList<MyVacationPeriod> Periods, IReadOnlyList<MyVacationSchedule> Schedules);
