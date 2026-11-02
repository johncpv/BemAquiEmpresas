using System.Globalization;
using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>
/// Tipo do desligamento (FLH-05 CA 2, linhas da matriz <c>hr.termination_matrix</c>): sem justa causa, por justa causa, pedido de demissão,
/// acordo entre as partes (CLT art. 484-A), término do contrato de experiência no prazo e término antecipado da experiência pela empresa
/// (art. 479) ou pelo empregado (art. 480).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TerminationKind>))]
public enum TerminationKind
{
    WithoutCause = 1,
    WithCause = 2,
    Resignation = 3,
    MutualAgreement = 4,
    ContractEnd = 5,
    EarlyEndByEmployer = 6,
    EarlyEndByEmployee = 7,
}

/// <summary>Aviso prévio (CA 1 e 3): trabalhado (pago no saldo de salário), indenizado (ou não cumprido, no pedido de demissão) ou dispensado.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TerminationNoticeKind>))]
public enum TerminationNoticeKind
{
    Worked = 1,
    Indemnified = 2,
    Waived = 3,
}

/// <summary>Situação da rescisão (<c>hr.terminations.status</c>): rascunho, calculada, fechada (folha de rescisão fechada) e cancelada (desligamento estornado).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TerminationStatus>))]
public enum TerminationStatus
{
    Draft,
    Calculated,
    Closed,
    Cancelled,
}

/// <summary>O aviso prévio na matriz: integral, metade (acordo, art. 484-A), sem aviso, ou o desconto do aviso não cumprido (pedido de demissão).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TerminationNoticeRule>))]
public enum TerminationNoticeRule
{
    Full = 1,
    Half = 2,
    None = 3,
    Deduction = 4,
}

/// <summary>As férias na matriz: vencidas e proporcionais com 1/3, ou só as vencidas com 1/3 (justa causa, Súmula 171 do TST).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TerminationVacationRule>))]
public enum TerminationVacationRule
{
    ExpiredAndProportional = 1,
    ExpiredOnly = 2,
}

/// <summary>Uma linha da matriz "tipo × verbas" (<c>hr.termination_matrix</c>): o aviso, as férias, o 13º proporcional e o percentual da multa do FGTS.</summary>
public sealed record TerminationMatrixRow(TerminationKind Kind, TerminationNoticeRule Notice, TerminationVacationRule Vacation, bool Thirteenth, decimal FgtsFinePct);

/// <summary>
/// Parâmetros da rescisão (<c>hr.parameters</c>, pré-configurados pela CLT, "A confirmar com a contadora" e com o jurídico, ADR 0042): a
/// matriz por tipo, o aviso proporcional (<c>hr.notice_days_rule</c>, Lei 12.506/2011: 30 dias + 3 por ano completo, até 90), o prazo do
/// pagamento (<c>hr.termination_payment_days</c>, CLT art. 477 §6º: 10 dias corridos) e o banco de horas negativo
/// (<c>hr.hour_bank_termination</c>: só descontado quando o acordo permite).
/// </summary>
public sealed record TerminationParameters(
    IReadOnlyList<TerminationMatrixRow> Matrix,
    int NoticeBaseDays,
    int NoticeDaysPerYear,
    int NoticeMaxDays,
    int PaymentDays,
    bool DiscountNegativeHourBank)
{
    /// <summary>A matriz da CLT (PO 21/10/2026, revisão pré-Fases 4 a 8, ponto 22).</summary>
    public static IReadOnlyList<TerminationMatrixRow> DefaultMatrix { get; } =
    [
        new(TerminationKind.WithoutCause, TerminationNoticeRule.Full, TerminationVacationRule.ExpiredAndProportional, true, 40m),
        new(TerminationKind.WithCause, TerminationNoticeRule.None, TerminationVacationRule.ExpiredOnly, false, 0m),
        new(TerminationKind.Resignation, TerminationNoticeRule.Deduction, TerminationVacationRule.ExpiredAndProportional, true, 0m),
        new(TerminationKind.MutualAgreement, TerminationNoticeRule.Half, TerminationVacationRule.ExpiredAndProportional, true, 20m),
        new(TerminationKind.ContractEnd, TerminationNoticeRule.None, TerminationVacationRule.ExpiredAndProportional, true, 0m),
        new(TerminationKind.EarlyEndByEmployer, TerminationNoticeRule.None, TerminationVacationRule.ExpiredAndProportional, true, 40m),
        new(TerminationKind.EarlyEndByEmployee, TerminationNoticeRule.None, TerminationVacationRule.ExpiredAndProportional, true, 0m),
    ];

    public static TerminationParameters Default { get; } = new(DefaultMatrix, 30, 3, 90, 10, false);

    public TerminationMatrixRow Row(TerminationKind kind) => Matrix.FirstOrDefault(r => r.Kind == kind) ?? DefaultMatrix.First(r => r.Kind == kind);
}

/// <summary>
/// Rescisão (<c>hr.terminations</c>, FLH-05): o tipo, o aviso (e os dias pelo <c>hr.notice_days_rule</c>), a data do desligamento (CAD-04b), o
/// fim previsto do contrato de experiência (arts. 479 e 480), o saldo do FGTS digitado do extrato e a data dele (<c>[PersonalData]</c>), a folha
/// de rescisão calculada e a situação. Nunca é apagada: o estorno do desligamento (CA 10) cancela com quem, quando e o motivo.
/// </summary>
[DocumentName("A rescisão")]
public sealed class EmployeeTermination : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid EmployeeId { get; init; }

    public TerminationKind Kind { get; set; }

    public TerminationNoticeKind NoticeKind { get; set; }

    /// <summary>Dias do aviso pelo <c>hr.notice_days_rule</c> no cálculo (os pagos ficam na memória: no acordo, a metade).</summary>
    public int NoticeDays { get; set; }

    public DateOnly TerminationDate { get; set; }

    /// <summary>Fim previsto do contrato de experiência (término antecipado, arts. 479 e 480).</summary>
    public DateOnly? ContractEndDate { get; set; }

    /// <summary>Saldo do FGTS para fins rescisórios, digitado do extrato (CA 4): base da multa.</summary>
    [PersonalData]
    public decimal? FgtsBalance { get; set; }

    public DateOnly? FgtsStatementDate { get; set; }

    public Guid? PayrollRunId { get; set; }

    public TerminationStatus Status { get; set; } = TerminationStatus.Draft;

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? CalculatedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    /// <summary>Motivo do estorno do desligamento (texto livre do RH).</summary>
    [PersonalData]
    public string? CancelReason { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Regras da rescisão sem banco (FLH-05): a matriz vem dos parâmetros (<see cref="TerminationParameters"/>), não do código; aqui ficam os
/// dias do aviso proporcional, a projeção do aviso indenizado, os avos das férias proporcionais, o prazo do pagamento, o aviso mais comum de
/// cada tipo (Propositivo) e os rótulos. Os números fixos são texto da lei: metade dos dias restantes (arts. 479 e 480), a fração de 15 dias
/// do avo das férias (art. 146, parágrafo único) e o 1/3 constitucional.
/// </summary>
public static class TerminationPolicy
{
    public const string SimulationCode = "termination_calculated_simulation";
    public const string SimulationMessage = "Rescisão calculada em conferência: feche a rescisão lançada.";
    public const string LatePaymentMessage = "Pagamento depois do prazo legal: há multa. Confirme com a contadora.";
    public const string FgtsBalanceCode = "termination_fgts_balance_required";
    public const string FgtsBalanceMessage = "Informe o saldo do FGTS do extrato para calcular a multa.";
    public const string PaidCode = "termination_paid";
    public const string PaidMessage = "A rescisão já foi paga: fale com a contadora para o estorno.";
    public const string ClosedCode = "termination_run_closed";
    public const string ClosedMessage = "Reabra a folha de rescisão antes.";
    public const string NotTerminatedCode = "employee_not_terminated";
    public const string TimesheetOpenCode = "termination_timesheet_open";
    public const string EmptySimulationMessage = "Escolha o tipo e a data para simular.";

    /// <summary>Fração do mês que conta como avo nas férias proporcionais (CLT art. 146, parágrafo único: mais de 14 dias).</summary>
    public const int AvoMinDays = 15;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string Date(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Anos completos de serviço entre a admissão e a data.</summary>
    public static int CompletedYears(DateOnly admission, DateOnly date)
    {
        var years = date.Year - admission.Year;
        if (admission.AddYears(years) > date)
        {
            years--;
        }

        return Math.Max(years, 0);
    }

    /// <summary>Dias do aviso prévio (Lei 12.506/2011, <c>hr.notice_days_rule</c>): a base mais os dias por ano completo, até o máximo.</summary>
    public static int NoticeDays(DateOnly admission, DateOnly termination, TerminationParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return Math.Clamp(p.NoticeBaseDays + (p.NoticeDaysPerYear * CompletedYears(admission, termination)), 0, Math.Max(p.NoticeMaxDays, p.NoticeBaseDays));
    }

    /// <summary>O aviso mais comum do tipo (Propositivo): indenizado na dispensa e no acordo, trabalhado no pedido de demissão, dispensado no resto.</summary>
    public static TerminationNoticeKind DefaultNotice(TerminationKind kind) => kind switch
    {
        TerminationKind.WithoutCause or TerminationKind.MutualAgreement => TerminationNoticeKind.Indemnified,
        TerminationKind.Resignation => TerminationNoticeKind.Worked,
        _ => TerminationNoticeKind.Waived,
    };

    /// <summary>O tipo exige o fim previsto do contrato de experiência (metade dos dias restantes).</summary>
    public static bool NeedsContractEnd(TerminationKind kind) => kind is TerminationKind.EarlyEndByEmployer or TerminationKind.EarlyEndByEmployee;

    /// <summary>Dias de aviso pagos (indenizados) pela regra da matriz: integral, a metade no acordo, ou nenhum.</summary>
    public static decimal PaidNoticeDays(TerminationNoticeRule rule, TerminationNoticeKind notice, int days) =>
        notice != TerminationNoticeKind.Indemnified ? 0m : rule switch
        {
            TerminationNoticeRule.Full => days,
            TerminationNoticeRule.Half => days / 2m,
            _ => 0m,
        };

    /// <summary>
    /// Fim projetado do contrato (OJ 82 da SDI-1 do TST): o aviso indenizado conta como tempo de serviço para os avos de férias e de 13º; o
    /// meio dia da metade do acordo não projeta.
    /// </summary>
    public static DateOnly ProjectedEnd(DateOnly termination, decimal paidNoticeDays) => termination.AddDays((int)decimal.Floor(paidNoticeDays));

    /// <summary>
    /// Avos de um período (férias proporcionais, art. 146): os meses completos contados do início e a fração restante de 15 dias ou mais
    /// como mais um, até 12.
    /// </summary>
    public static int Avos(DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            return 0;
        }

        var count = 0;
        var cursor = start;
        while (count < 12 && cursor.AddMonths(1).AddDays(-1) <= end)
        {
            count++;
            cursor = cursor.AddMonths(1);
        }

        if (count < 12 && end.DayNumber - cursor.DayNumber + 1 >= AvoMinDays)
        {
            count++;
        }

        return count;
    }

    /// <summary>Prazo do pagamento (CA 8, <c>hr.termination_payment_days</c>): dias corridos depois do desligamento.</summary>
    public static DateOnly PaymentDeadline(DateOnly termination, TerminationParameters p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return termination.AddDays(Math.Max(p.PaymentDays, 0));
    }

    public static string KindLabel(TerminationKind kind) => kind switch
    {
        TerminationKind.WithoutCause => "Dispensa sem justa causa",
        TerminationKind.WithCause => "Dispensa por justa causa",
        TerminationKind.Resignation => "Pedido de demissão",
        TerminationKind.MutualAgreement => "Acordo entre as partes (art. 484-A)",
        TerminationKind.ContractEnd => "Término do contrato de experiência no prazo",
        TerminationKind.EarlyEndByEmployer => "Término antecipado da experiência pela empresa",
        _ => "Término antecipado da experiência pelo empregado",
    };

    public static string NoticeLabel(TerminationNoticeKind kind) => kind switch
    {
        TerminationNoticeKind.Worked => "Trabalhado",
        TerminationNoticeKind.Indemnified => "Indenizado",
        _ => "Dispensado",
    };

    public static string StatusLabel(TerminationStatus status) => status switch
    {
        TerminationStatus.Draft => "Rascunho",
        TerminationStatus.Calculated => "Calculada",
        TerminationStatus.Closed => "Fechada",
        _ => "Cancelada",
    };

    /// <summary>A chave do tipo nos parâmetros da matriz ("hr.termination_matrix.without_cause.notice").</summary>
    public static string KindKey(TerminationKind kind) => kind switch
    {
        TerminationKind.WithoutCause => "without_cause",
        TerminationKind.WithCause => "with_cause",
        TerminationKind.Resignation => "resignation",
        TerminationKind.MutualAgreement => "mutual_agreement",
        TerminationKind.ContractEnd => "contract_end",
        TerminationKind.EarlyEndByEmployer => "early_end_employer",
        _ => "early_end_employee",
    };

    public static string Days(decimal days) => days.ToString("0.#", PtBr) + (days == 1m ? " dia" : " dias");
}
