using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>
/// Situação do período aquisitivo (FLH-03 CA 1): em aquisição, a gozar (completo, com saldo, dentro do concessivo), vencido (o concessivo
/// passou com saldo: os dias gozados depois dele são pagos em dobro) e encerrado (sem saldo ou reiniciado por afastamento, CA 2).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VacationPeriodStatus>))]
public enum VacationPeriodStatus
{
    Accruing,
    Available,
    Expired,
    Closed,
}

/// <summary>Situação da programação de férias (CA 4, 9 e 11): programada, paga (folha de férias fechada), gozada e cancelada.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VacationScheduleStatus>))]
public enum VacationScheduleStatus
{
    Scheduled,
    Paid,
    Taken,
    Cancelled,
}

/// <summary>Como o INSS das férias entra nas competências (<c>hr.vacation_taxation</c>, CA 6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VacationTaxation>))]
public enum VacationTaxation
{
    /// <summary>INSS de férias + 1/3 rateado pelas competências do gozo, junto com o salário; IRRF em separado no pagamento (pré-configurado).</summary>
    SplitByMonth = 1,

    /// <summary>INSS de férias + 1/3 inteiro na competência do início do gozo.</summary>
    StartMonth = 2,
}

/// <summary>
/// Período aquisitivo (<c>hr.vacation_periods</c>, FLH-03 CA 1 e 2): refeito pela <see cref="VacationPolicy"/> a partir da admissão, das faltas
/// injustificadas do espelho fechado e dos afastamentos; <see cref="Sequence"/> é a ordem desde a admissão (a chave da atualização). Os dias
/// gozados e vendidos vêm das programações não canceladas. O motivo do reinício é <c>[PersonalData]</c> (fala do afastamento).
/// </summary>
[DocumentName("O período aquisitivo")]
public sealed class VacationPeriod : Entity, IAuditable
{
    public Guid EmployeeId { get; init; }

    public int Sequence { get; init; }

    public DateOnly StartsOn { get; set; }

    public DateOnly EndsOn { get; set; }

    /// <summary>Faltas injustificadas no período (dias "Falta" dos espelhos fechados).</summary>
    public int Absences { get; set; }

    public int EntitledDays { get; set; }

    public int TakenDays { get; set; }

    public int SoldDays { get; set; }

    /// <summary>Fim do período concessivo: 12 meses depois do fim do aquisitivo (CLT art. 134).</summary>
    public DateOnly ConcessionEndsOn { get; set; }

    public VacationPeriodStatus Status { get; set; }

    /// <summary>"Afastamento pela previdência por mais de 180 dias" (CA 2): o período foi encerrado e outro começou no retorno.</summary>
    [PersonalData]
    public string? RestartedReason { get; set; }

    /// <summary>Data em que o próximo período começou (o retorno do afastamento).</summary>
    public DateOnly? RestartedOn { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int Balance => Math.Max(EntitledDays - TakenDays - SoldDays, 0);
}

/// <summary>
/// Programação de férias (<c>hr.vacation_schedules</c>, CA 4 e 5): o período aquisitivo, o início, os dias de gozo, os dias vendidos (abono
/// pecuniário), o adiantamento do 13º pedido (calculado na FLH-04), o vencimento do pagamento (<c>hr.vacation_payment_days_before</c>) e a folha
/// de férias calculada. A divisão do INSS por competência (<see cref="CalculatedSplit"/>, jsonb) e o motivo do cancelamento são
/// <c>[PersonalData]</c>. Nunca é apagada: o cancelamento guarda quem, quando e o motivo (CA 11).
/// </summary>
[DocumentName("A programação de férias")]
public sealed class VacationSchedule : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid EmployeeId { get; init; }

    public Guid PeriodId { get; init; }

    public DateOnly StartsOn { get; init; }

    public int Days { get; init; }

    public int SoldDays { get; init; }

    public bool Advance13th { get; init; }

    public DateOnly PaymentDueOn { get; set; }

    public VacationScheduleStatus Status { get; set; } = VacationScheduleStatus.Scheduled;

    public Guid? PayrollRunId { get; set; }

    /// <summary>A parte das férias em cada competência (<see cref="VacationMonthSplit"/>): dias, base e INSS retido, base do FGTS (CA 10).</summary>
    [PersonalData]
    public string? CalculatedSplit { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    [PersonalData]
    public string? CancelReason { get; set; }

    public uint Version { get; set; }

    public DateOnly EndsOn => StartsOn.AddDays(Math.Max(Days, 1) - 1);
}

/// <summary>Faixa de faltas (<c>hr.vacation_absence_brackets</c>): até tantas faltas injustificadas, tantos dias de férias.</summary>
public sealed record VacationAbsenceBracket(int MaxAbsences, int Days);

/// <summary>
/// Parâmetros de férias (<c>hr.parameters</c>, pré-configurados pela CLT, "A confirmar com a contadora", ADR 0042): faixas de faltas (art.
/// 130), afastamentos que reiniciam o período (art. 133: previdência por mais de 6 meses, contínuos ou não, ou licença remunerada por mais
/// de 30 dias), fracionamento (art. 134 §1: até 3 períodos, um com 14 dias e os demais com 5), aviso (art. 135: 30 dias), pagamento (art.
/// 145: 2 dias antes), meses da média das variáveis e a tributação.
/// </summary>
public sealed record VacationParameters(
    IReadOnlyList<VacationAbsenceBracket> AbsenceBrackets,
    int InssLeaveMaxDays,
    int PaidLeaveMaxDays,
    int SickLeaveCompanyDays,
    int MaxParts,
    int FirstPartMinDays,
    int OtherPartMinDays,
    int NoticeDays,
    int PaymentDaysBefore,
    int AverageMonths,
    VacationTaxation Taxation)
{
    public static VacationParameters Default { get; } = new([new(5, 30), new(14, 24), new(23, 18), new(32, 12)], 180, 30, 15, 3, 14, 5, 30, 2, 12, VacationTaxation.SplitByMonth);
}

/// <summary>Afastamento no cálculo do período (CAD-04c1): início, fim (null = em aberto) e o tipo.</summary>
public sealed record VacationLeaveInput(DateOnly Start, DateOnly? End, AbsenceReason? Reason);

/// <summary>Entrada do cálculo dos períodos: admissão, desligamento, hoje, afastamentos, os dias de falta injustificada e os parâmetros.</summary>
public sealed record VacationPlanInput(DateOnly Admission, DateOnly? Termination, DateOnly Today, IReadOnlyList<VacationLeaveInput> Leaves, IReadOnlyCollection<DateOnly> Absences, VacationParameters Parameters);

/// <summary>Período calculado: ordem, início e fim, faltas, dias de direito, fim do concessivo, o reinício (CA 2) e a memória.</summary>
public sealed record VacationPeriodPlan(int Sequence, DateOnly Start, DateOnly End, int Absences, int EntitledDays, DateOnly ConcessionEnd, string? RestartReason, DateOnly? RestartedOn, IReadOnlyList<string> Memory);

/// <summary>Parte já programada de um período (não cancelada).</summary>
public sealed record VacationPart(DateOnly Start, int Days, int SoldDays)
{
    public DateOnly End => Start.AddDays(Math.Max(Days, 1) - 1);
}

/// <summary>O período como a validação enxerga: situação, fim, dias de direito, saldo e fim do concessivo.</summary>
public sealed record VacationPeriodView(VacationPeriodStatus Status, DateOnly End, int EntitledDays, int Balance, DateOnly ConcessionEnd);

/// <summary>Pedido de programação (CA 4): início, dias de gozo e dias vendidos.</summary>
public sealed record VacationRequest(DateOnly Start, int Days, int SoldDays);

/// <summary>Erro da validação (campo e mensagem com o valor do parâmetro) e os avisos que não travam (aviso de 30 dias, pagamento em dobro).</summary>
public sealed record VacationCheck(string? Field, string? Error, IReadOnlyList<string> Warnings);

/// <summary>Sugestão (Propositivo): o primeiro início permitido depois do aviso e os dias com o saldo do período.</summary>
public sealed record VacationSuggestion(DateOnly Start, int Days);

/// <summary>
/// Regras de férias sem banco (FLH-03, ADR 0039 §2): períodos aquisitivos com as faltas e os afastamentos (CA 1 e 2), situação e alerta de
/// vencimento (CA 3), validação da programação (CA 4) e a sugestão do início. Nenhum número legal no código: tudo de <see cref="VacationParameters"/>,
/// salvo o 1/3 do abono e os 2 dias antes de feriado ou repouso (texto da CLT, arts. 143 e 134 §3).
/// </summary>
public static class VacationPolicy
{
    /// <summary>Dias antes do fim do concessivo em que o vencimento vira alerta (CA 3).</summary>
    public const int DueAlertDays = 60;

    /// <summary>Dias antes de feriado ou repouso semanal em que as férias não começam (CLT art. 134 §3).</summary>
    public const int BlockedDaysBefore = 2;

    public const string NotAvailableCode = "vacation_period_not_available";
    public const string InvalidCode = "vacation_schedule_invalid";
    public const string RunClosedCode = "vacation_run_closed";
    public const string RunClosedMessage = "Reabra a folha de férias antes.";
    public const string SimulationCode = "vacation_calculated_simulation";
    public const string SimulationMessage = "Férias calculadas em conferência: feche a folha de férias lançada.";
    public const string LatePaymentMessage = "Pagamento depois do prazo legal: confirme com a contadora.";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Date(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Dias de direito pelas faltas injustificadas (art. 130): a primeira faixa que cobre as faltas; acima da última, nenhum.</summary>
    public static int EntitledDays(int absences, IReadOnlyList<VacationAbsenceBracket> brackets)
    {
        ArgumentNullException.ThrowIfNull(brackets);
        return brackets.OrderBy(b => b.MaxAbsences).FirstOrDefault(b => absences <= b.MaxAbsences)?.Days ?? 0;
    }

    public static DateOnly ConcessionEnd(DateOnly periodEnd) => periodEnd.AddYears(1);

    /// <summary>
    /// Os períodos desde a admissão até hoje (ou o desligamento): cada um de 12 meses, com as faltas e os dias de direito. Afastamento pela
    /// previdência (doença ou acidente, depois dos dias pagos pela empresa) que passa do limite, somando os do período, ou licença
    /// remunerada (falta justificada como afastamento) que passa do limite encerra o período sem direito, e o próximo começa no retorno.
    /// </summary>
    public static IReadOnlyList<VacationPeriodPlan> Plan(VacationPlanInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var p = input.Parameters;
        var limit = input.Termination is { } t && t < input.Today ? t : input.Today;
        var list = new List<VacationPeriodPlan>();
        DateOnly? next = input.Admission;
        for (var seq = 1; next is { } start && start <= limit && seq <= 100; seq++)
        {
            var end = start.AddYears(1).AddDays(-1);
            var (crossing, newStart, reason) = Restart(start, end, input.Leaves, p);
            var memory = new List<string>();
            if (seq > 1 && list[^1].RestartedOn == start)
            {
                memory.Add($"Começou no retorno do afastamento, em {Date(start)}.");
            }

            if (crossing is { } lost)
            {
                var absences = input.Absences.Count(d => d >= start && d <= lost);
                memory.Add(newStart is { } ns ? $"Período reiniciado em {Date(ns)}: {reason}." : $"Período encerrado em {Date(lost)}: {reason}; o próximo começa no retorno.");
                list.Add(new VacationPeriodPlan(seq, start, lost, absences, 0, ConcessionEnd(lost), reason, newStart, memory));
                next = newStart;
                continue;
            }

            var count = input.Absences.Count(d => d >= start && d <= end);
            var days = EntitledDays(count, p.AbsenceBrackets);
            memory.Add($"{count} {(count == 1 ? "falta injustificada" : "faltas injustificadas")}: {days} dias de direito (parâmetro hr.vacation_absence_brackets).");
            list.Add(new VacationPeriodPlan(seq, start, end, count, days, ConcessionEnd(end), null, null, memory));
            next = end.AddDays(1);
        }

        return list;
    }

    /// <summary>
    /// Situação do período (CA 1): reiniciado, sem saldo ou interrompido pelo desligamento (o proporcional é da FLH-05) é encerrado; até o fim,
    /// em aquisição; depois do concessivo, vencido.
    /// </summary>
    public static VacationPeriodStatus StatusOf(DateOnly end, DateOnly concessionEnd, bool restarted, int balance, DateOnly today, DateOnly? termination = null) =>
        restarted || (termination is { } t && t < end && today > t) ? VacationPeriodStatus.Closed
        : today <= end ? VacationPeriodStatus.Accruing
        : balance <= 0 ? VacationPeriodStatus.Closed
        : today > concessionEnd ? VacationPeriodStatus.Expired
        : VacationPeriodStatus.Available;

    /// <summary>"Vence em {data}" (CA 1 e 3): a gozar com o fim do concessivo a até 60 dias.</summary>
    public static bool DueSoon(VacationPeriodStatus status, DateOnly concessionEnd, DateOnly today) =>
        status == VacationPeriodStatus.Available && concessionEnd.DayNumber - today.DayNumber <= DueAlertDays;

    /// <summary>
    /// Validação da programação (CA 4), na ordem da tela: o período completo e com saldo, o abono (até 1/3 dos dias de direito), o saldo,
    /// o fracionamento (até N partes, a primeira com o mínimo maior e as outras com o menor, sem sobrar dias que não caibam), o início
    /// depois do fim do período, sem sobrepor outras férias e fora dos 2 dias antes de feriado ou repouso. Avisos: início a menos que o aviso
    /// e dias depois do concessivo (em dobro).
    /// </summary>
    public static VacationCheck Validate(VacationPeriodView period, IReadOnlyList<VacationPart> periodParts, IReadOnlyList<VacationPart> employeeParts, VacationRequest request,
        Func<DateOnly, bool> restOrHoliday, DateOnly today, DateOnly? termination, VacationParameters p)
    {
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(periodParts);
        ArgumentNullException.ThrowIfNull(employeeParts);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(restOrHoliday);
        ArgumentNullException.ThrowIfNull(p);
        VacationCheck Fail(string field, string message) => new(field, message, []);
        if (period.Status == VacationPeriodStatus.Accruing)
        {
            return Fail("periodId", $"O período aquisitivo ainda não terminou: as férias dele começam a partir de {Date(period.End.AddDays(1))}.");
        }

        if (period.Status == VacationPeriodStatus.Closed || period.Balance <= 0)
        {
            return Fail("periodId", "Este período não tem saldo de férias.");
        }

        if (request.Days < 1)
        {
            return Fail("days", "Informe os dias de férias.");
        }

        if (request.SoldDays < 0)
        {
            return Fail("soldDays", "Os dias vendidos não podem ser negativos.");
        }

        var maxSold = period.EntitledDays / 3;
        if (periodParts.Sum(x => x.SoldDays) + request.SoldDays > maxSold)
        {
            return Fail("soldDays", $"O abono pecuniário é de até {maxSold} dias (1/3 dos {period.EntitledDays} dias de direito).");
        }

        if (request.Days + request.SoldDays > period.Balance)
        {
            return Fail("days", $"O período tem {period.Balance} dias de saldo.");
        }

        if (periodParts.Count + 1 > p.MaxParts)
        {
            return Fail("days", $"As férias podem ser divididas em até {p.MaxParts} períodos.");
        }

        if (!periodParts.Any(x => x.Days >= p.FirstPartMinDays) && request.Days < p.FirstPartMinDays)
        {
            return Fail("days", $"O primeiro período precisa ter pelo menos {p.FirstPartMinDays} dias.");
        }

        if (request.Days < p.OtherPartMinDays)
        {
            return Fail("days", $"Cada período precisa ter pelo menos {p.OtherPartMinDays} dias.");
        }

        var rest = period.Balance - request.Days - request.SoldDays;
        if (rest > 0 && periodParts.Count + 1 == p.MaxParts)
        {
            return Fail("days", $"Sobrariam {rest} dias sem período: as férias podem ser divididas em até {p.MaxParts} períodos.");
        }

        if (rest > 0 && rest < p.OtherPartMinDays)
        {
            return Fail("days", $"Sobrariam {rest} dias: cada período precisa ter pelo menos {p.OtherPartMinDays} dias.");
        }

        if (request.Start <= period.End)
        {
            return Fail("startsOn", $"As férias deste período começam a partir de {Date(period.End.AddDays(1))}.");
        }

        if (termination is { } t && request.Start > t)
        {
            return Fail("startsOn", "O início fica depois do desligamento.");
        }

        var end = request.Start.AddDays(request.Days - 1);
        if (employeeParts.FirstOrDefault(x => x.Start <= end && request.Start <= x.End) is { } overlap)
        {
            return Fail("startsOn", $"Já há férias programadas de {Date(overlap.Start)} a {Date(overlap.End)}.");
        }

        if (Blocked(request.Start, restOrHoliday) is { } blocked)
        {
            return Fail("startsOn", $"As férias não podem começar nos {BlockedDaysBefore} dias antes de feriado ou do repouso semanal ({Date(blocked)}).");
        }

        var warnings = new List<string>();
        var notice = request.Start.DayNumber - today.DayNumber;
        if (notice < p.NoticeDays)
        {
            warnings.Add($"O aviso de férias precisa sair {p.NoticeDays} dias antes do início (parâmetro hr.vacation_notice_days): faltam {Math.Max(notice, 0)} dias.");
        }

        if (end > period.ConcessionEnd)
        {
            var late = end.DayNumber - Math.Max(period.ConcessionEnd.DayNumber, request.Start.DayNumber - 1);
            warnings.Add($"Férias depois do fim do período concessivo ({Date(period.ConcessionEnd)}): {late} {(late == 1 ? "dia é pago" : "dias são pagos")} em dobro (CLT art. 137).");
        }

        return new VacationCheck(null, null, warnings);
    }

    /// <summary>O feriado ou o repouso que impede o início (o dia seguinte ou o outro), ou null.</summary>
    public static DateOnly? Blocked(DateOnly start, Func<DateOnly, bool> restOrHoliday)
    {
        ArgumentNullException.ThrowIfNull(restOrHoliday);
        for (var i = 1; i <= BlockedDaysBefore; i++)
        {
            if (restOrHoliday(start.AddDays(i)))
            {
                return start.AddDays(i);
            }
        }

        return null;
    }

    /// <summary>
    /// Sugestão (Propositivo): o primeiro dia depois do aviso (e do fim do período) que não é feriado nem repouso e não fica nos 2 dias antes
    /// deles; os dias são o saldo (até 30).
    /// </summary>
    public static VacationSuggestion Suggest(DateOnly today, DateOnly periodEnd, int balance, Func<DateOnly, bool> restOrHoliday, VacationParameters p)
    {
        ArgumentNullException.ThrowIfNull(restOrHoliday);
        ArgumentNullException.ThrowIfNull(p);
        var start = today.AddDays(p.NoticeDays);
        if (start <= periodEnd)
        {
            start = periodEnd.AddDays(1);
        }

        for (var i = 0; i < 60 && (restOrHoliday(start) || Blocked(start, restOrHoliday) is not null); i++)
        {
            start = start.AddDays(1);
        }

        return new VacationSuggestion(start, Math.Clamp(balance, 0, 30));
    }

    /// <summary>Vencimento do pagamento (CA 9): o início menos os dias do parâmetro (o dia não útil antecipa, no serviço).</summary>
    public static DateOnly PaymentDue(DateOnly start, VacationParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return start.AddDays(-Math.Max(p.PaymentDaysBefore, 0));
    }

    /// <summary>Dias de cada competência (calendário) entre o início e o fim, em ordem.</summary>
    public static IReadOnlyList<(DateOnly Month, int Days)> DaysByMonth(DateOnly start, DateOnly end)
    {
        var list = new List<(DateOnly, int)>();
        for (var d = start; d <= end;)
        {
            var month = new DateOnly(d.Year, d.Month, 1);
            var last = month.AddMonths(1).AddDays(-1);
            var to = last < end ? last : end;
            list.Add((month, to.DayNumber - d.DayNumber + 1));
            d = to.AddDays(1);
        }

        return list;
    }

    public static string StatusLabel(VacationPeriodStatus status, DateOnly concessionEnd, DateOnly today) => status switch
    {
        VacationPeriodStatus.Accruing => "Em aquisição",
        VacationPeriodStatus.Available when DueSoon(status, concessionEnd, today) => $"Vence em {Date(concessionEnd)}",
        VacationPeriodStatus.Available => "A gozar",
        VacationPeriodStatus.Expired => "Vencido",
        _ => "Encerrado",
    };

    public static string ScheduleStatusLabel(VacationScheduleStatus status) => status switch
    {
        VacationScheduleStatus.Scheduled => "Programada",
        VacationScheduleStatus.Paid => "Paga",
        VacationScheduleStatus.Taken => "Gozada",
        _ => "Cancelada",
    };

    public static string WriteSplit(IReadOnlyList<VacationMonthSplit> split) => JsonSerializer.Serialize(split, Json);

    public static IReadOnlyList<VacationMonthSplit> ReadSplit(string? json) => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<List<VacationMonthSplit>>(json, Json) ?? [];

    public static string Count(int n, string one, string many) => n.ToString(PtBr) + " " + (n == 1 ? one : many);

    private static (DateOnly? Crossing, DateOnly? NewStart, string? Reason) Restart(DateOnly start, DateOnly end, IReadOnlyList<VacationLeaveInput> leaves, VacationParameters p)
    {
        var far = DateOnly.MaxValue.AddDays(-1);
        (DateOnly Crossing, DateOnly? NewStart, string Reason)? best = null;
        var inssDays = 0;
        foreach (var leave in leaves.OrderBy(l => l.Start))
        {
            var leaveEnd = leave.End ?? far;
            if (leave.Start > end || leaveEnd < start)
            {
                continue;
            }

            DateOnly? Return() => leave.End is { } e ? e.AddDays(1) : null;
            if (leave.Reason is AbsenceReason.MedicalCertificate or AbsenceReason.WorkAccident)
            {
                var from = leave.Start.AddDays(Math.Max(p.SickLeaveCompanyDays, 0));
                from = from > start ? from : start;
                var to = leaveEnd < end ? leaveEnd : end;
                var days = to < from ? 0 : to.DayNumber - from.DayNumber + 1;
                if (inssDays + days > p.InssLeaveMaxDays)
                {
                    var crossing = from.AddDays(p.InssLeaveMaxDays - inssDays);
                    if (best is null || crossing < best.Value.Crossing)
                    {
                        best = (crossing, Return(), $"afastamento pela previdência por mais de {p.InssLeaveMaxDays} dias, contínuos ou não (parâmetro hr.vacation_leave_rules)");
                    }

                    break;
                }

                inssDays += days;
            }
            else if (leave.Reason == AbsenceReason.JustifiedAbsence)
            {
                var from = leave.Start > start ? leave.Start : start;
                var to = leaveEnd < end ? leaveEnd : end;
                var days = to < from ? 0 : to.DayNumber - from.DayNumber + 1;
                if (days > p.PaidLeaveMaxDays)
                {
                    var crossing = from.AddDays(p.PaidLeaveMaxDays);
                    if (best is null || crossing < best.Value.Crossing)
                    {
                        best = (crossing, Return(), $"licença remunerada por mais de {p.PaidLeaveMaxDays} dias (parâmetro hr.vacation_leave_rules)");
                    }
                }
            }
        }

        return best is { } b ? (b.Crossing, b.NewStart, b.Reason) : (null, null, null);
    }
}
