using System.Globalization;
using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>Tipo da rubrica (RH-02a CA 1): provento soma, desconto subtrai e a informativa (FGTS) não entra no líquido.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollItemKind>))]
public enum PayrollItemKind
{
    Earning,
    Deduction,
    Information,
}

/// <summary>Tipo da folha (RH-02a CA 4): cada um com vencimento próprio no caixa (RH-02c).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollRunType>))]
public enum PayrollRunType
{
    Monthly,
    Advance,
    Vacation,
    Thirteenth1,
    Thirteenth2,
    Termination,
}

/// <summary>Folha lançada (valores da contadora, Fase 6) ou calculada (Fase 7, mesmas tabelas).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollRunMode>))]
public enum PayrollRunMode
{
    Entered,
    Calculated,
}

[JsonConverter(typeof(JsonStringEnumConverter<PayrollRunStatus>))]
public enum PayrollRunStatus
{
    Draft,
    Closed,
    Paid,
}

/// <summary>Guia de encargo da empresa (RH-02a CA 7): uma linha por guia, que vira um título a pagar na RH-02c (ADR 0036).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollChargeKind>))]
public enum PayrollChargeKind
{
    Fgts,
    Inss,
    Irrf,
    Union,
    Other,
}

/// <summary>
/// Rubrica (<c>hr.payroll_items</c>, RH-02a CA 1 e 2): código (até 10, único entre as não excluídas), nome, tipo e as marcas de base
/// (informativas, usadas pela folha calculada da Fase 7). A lista padrão nasce do seed pela <see cref="SeedKey"/>, "A confirmar com a
/// contadora" até alguém marcar como conferida (<see cref="ConfirmedAt"/>, ADR 0042). O cadastro não é dado pessoal.
/// </summary>
[DocumentName("A rubrica")]
public sealed class PayrollItem : Entity, IAuditable, ISoftDeletable, IConcurrencyVersioned
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public PayrollItemKind Kind { get; set; }

    public bool InssBase { get; set; }

    public bool FgtsBase { get; set; }

    public bool IrrfBase { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>"Origem no ponto" (RH-01c CA 16): o total do espelho que preenche a referência da linha ("Usar horas do espelho").</summary>
    public PayrollTimesheetSource? TimesheetSource { get; set; }

    public string? SeedKey { get; init; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void Confirm(Guid? by, DateTimeOffset at)
    {
        ConfirmedAt = at;
        ConfirmedBy = by;
    }

    /// <summary>Código, tipo ou base mudou: a conferência valia para o de antes (ADR 0042 §1).</summary>
    public void ResetConfirmation()
    {
        ConfirmedAt = null;
        ConfirmedBy = null;
    }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }
}

/// <summary>
/// Folha da competência (<c>hr.payroll_runs</c>, RH-02a CA 4): competência (primeiro dia do mês), tipo, modo e situação. Nenhum valor
/// aqui: o resumo da folha sai para quem tem só <c>hr.view</c> (CA 12).
/// </summary>
[DocumentName("A folha")]
public sealed class PayrollRun : Entity, IAuditable, IConcurrencyVersioned
{
    public DateOnly Period { get; init; }

    public PayrollRunType Type { get; init; }

    public PayrollRunMode Mode { get; init; } = PayrollRunMode.Entered;

    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;

    /// <summary>Sequência opcional (folhas complementares, depois).</summary>
    public int? Number { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? ClosedAt { get; set; }

    public Guid? ClosedBy { get; set; }

    /// <summary>Data de pagamento da folha (RH-02c CA 1): o vencimento dos títulos de salário.</summary>
    public DateOnly? PaymentDate { get; set; }

    /// <summary>Totais congelados no fechamento (RH-02c CA 2): o líquido dos salários e o total das guias. Dado da folha, mascarado na auditoria.</summary>
    [PersonalData]
    public decimal? ClosedNet { get; set; }

    [PersonalData]
    public decimal? ClosedCharges { get; set; }

    /// <summary>Quantos títulos o fechamento gerou (salários e guias).</summary>
    public int? ClosedPayables { get; set; }

    /// <summary>Todos os títulos pagos (RH-02c CA 12).</summary>
    public DateTimeOffset? PaidAt { get; set; }

    /// <summary>Motivo da última reabertura (RH-02c CA 13).</summary>
    public string? ReopenReason { get; set; }

    public DateTimeOffset? ReopenedAt { get; set; }

    public Guid? ReopenedBy { get; set; }

    public uint Version { get; set; }
}

/// <summary>O que a linha da folha prevista representa (RH-02c CA 14).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollForecastKind>))]
public enum PayrollForecastKind
{
    Salary,
    Charge,
    Thirteenth1,
    Thirteenth2,
    Vacation,
}

/// <summary>
/// Linha da folha prevista (<c>hr.payroll_forecasts</c>, RH-02c CA 14 e 15, ADR 0036 §4): um título <c>Planned</c> estimado no Finance
/// (origem <c>PayrollForecast</c>, <c>origin_id</c> = esta linha) por funcionário e mês, por guia e mês, pelo 13º e pelas férias em rascunho.
/// A linha é cancelada (e o título com ela) quando sai da janela ou a folha real do mês e do tipo fecha; nunca é apagada.
/// </summary>
[DocumentName("A previsão da folha")]
public sealed class PayrollForecast : Entity, IAuditable
{
    public DateOnly Period { get; init; }

    public PayrollForecastKind Kind { get; init; }

    public Guid? EmployeeId { get; init; }

    public PayrollChargeKind? ChargeKind { get; init; }

    [PersonalData]
    public decimal Amount { get; set; }

    public DateOnly DueDate { get; set; }

    /// <summary>Folha de onde saiu o valor (a última Mensal fechada, ou a de Férias em rascunho).</summary>
    public Guid? SourceRunId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public string? CancelReason { get; set; }

    /// <summary>Chave da linha: mês, tipo e o funcionário ou a guia.</summary>
    public string Key => PayrollForecastRules.Key(Period, Kind, EmployeeId, ChargeKind);
}

/// <summary>
/// Holerite (<c>hr.payslips</c>, RH-02a CA 5 e 6): um por funcionário na folha. Totais, bases e o FGTS do mês são dado pessoal
/// (<c>[PersonalData]</c>): só por rota <c>/sensitive</c>, mascarados na auditoria e nunca em log ou outbox (CA 12 e 13).
/// </summary>
[DocumentName("O holerite")]
public sealed class Payslip : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid PayrollRunId { get; init; }

    public Guid EmployeeId { get; init; }

    [PersonalData]
    public decimal Gross { get; set; }

    [PersonalData]
    public decimal Deductions { get; set; }

    [PersonalData]
    public decimal Net { get; set; }

    [PersonalData]
    public decimal InssBase { get; set; }

    [PersonalData]
    public decimal FgtsBase { get; set; }

    [PersonalData]
    public decimal IrrfBase { get; set; }

    /// <summary>FGTS do mês que a contadora calculou (informativo; a guia do FGTS é da empresa, CA 7).</summary>
    [PersonalData]
    public decimal FgtsAmount { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// PDF guardado (RH-02b CA 3): arquivo do sistema (<c>shared.attachments</c>, dono <c>Payslip</c>) gerado com a folha fechada; volta a
    /// nulo quando a folha é reaberta ou o holerite muda, e o próximo download refaz.
    /// </summary>
    public Guid? PdfAttachmentId { get; set; }

    /// <summary>Último aviso por e-mail enfileirado (RH-02b CA 7 a 9): a situação sai do <c>email_logs</c> por <see cref="NoticeEmailLogId"/>.</summary>
    public DateTimeOffset? NotifiedAt { get; set; }

    public Guid? NoticeEmailLogId { get; set; }

    /// <summary>Primeira leitura pelo próprio funcionário em "Meus holerites" (CA 5): "Visto em {data}".</summary>
    public DateTimeOffset? FirstViewedAt { get; set; }

    /// <summary>O holerite mudou depois do aviso (CA 10): "Alterado depois do aviso", até o funcionário ser avisado de novo.</summary>
    public bool ChangedAfterNotice { get; set; }

    public uint Version { get; set; }

    /// <summary>Totais pelas linhas (CA 6, ADR 0019 §9).</summary>
    public void ApplyTotals(PayslipTotals totals)
    {
        ArgumentNullException.ThrowIfNull(totals);
        Gross = totals.Gross;
        Deductions = totals.Deductions;
        Net = totals.Net;
    }
}

/// <summary>
/// Linha do holerite (<c>hr.payslip_lines</c>): a rubrica (com o código e o nome do momento, para o histórico sobreviver à troca do
/// código), a referência em texto (até 20: "30 dias", "12:30 h") e o valor. Referência e valor são dado pessoal.
/// </summary>
public sealed class PayslipLine : Entity, IAuditable
{
    public Guid PayslipId { get; init; }

    public Guid PayrollItemId { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public PayrollItemKind Type { get; init; }

    [PersonalData]
    public string? Reference { get; init; }

    [PersonalData]
    public decimal Amount { get; init; }

    public int Sort { get; init; }
}

/// <summary>
/// Guia de encargo da empresa na folha (<c>hr.payroll_run_charges</c>, CA 7): tipo, descrição, valor, vencimento e o órgão favorecido
/// (terceiro do <c>IParties</c>). O valor é <c>[PersonalData]</c>: numa empresa pequena, a guia revela a folha.
/// </summary>
[DocumentName("A guia")]
public sealed class PayrollRunCharge : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid PayrollRunId { get; init; }

    public PayrollChargeKind Kind { get; set; }

    public string Description { get; set; } = string.Empty;

    [PersonalData]
    public decimal Amount { get; set; }

    public DateOnly DueDate { get; set; }

    public Guid? PayeePartyId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }
}

/// <summary>
/// Parâmetro do RH (<c>hr.parameters</c>, ADR 0016 M6, ADR 0042): número (<see cref="Value"/>) ou texto (<see cref="Text"/>, o id do
/// órgão favorecido). Os legais (vencimentos das guias) levam a conferência: <see cref="ConfirmedAt"/> nulo é "A confirmar com a
/// contadora"; trocar o valor volta para "A confirmar".
/// </summary>
public sealed class HrParameter : Entity, IAuditable
{
    public string Key { get; init; } = string.Empty;

    public decimal? Value { get; set; }

    public string? Text { get; set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public void Confirm(Guid? by, DateTimeOffset at)
    {
        ConfirmedAt = at;
        ConfirmedBy = by;
    }

    public void ResetConfirmation()
    {
        ConfirmedAt = null;
        ConfirmedBy = null;
    }
}

/// <summary>Totais do holerite: proventos, descontos e líquido (= proventos − descontos).</summary>
public sealed record PayslipTotals(decimal Gross, decimal Deductions, decimal Net);

/// <summary>Linha para os totais e a validação, sem banco.</summary>
public sealed record PayslipLineValue(PayrollItemKind Type, decimal Amount, string? Reference);

/// <summary>Regras da folha lançada sem banco (RH-02a): nada de cálculo legal, só soma, validação e datas.</summary>
public static class PayrollRules
{
    public const int CodeMaxLength = 10;
    public const int NameMaxLength = 100;
    public const int ReferenceMaxLength = 20;
    public const int ChargeDescriptionMaxLength = 100;
    public const int MaxLines = 100;
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    public const string ClosedCode = "payroll_run_closed";
    public const string DuplicateCode = "payroll_run_exists";
    public const string NegativeNetCode = "payslip_negative_net";
    public const string ItemInUseCode = "payroll_item_in_use";
    public const string ItemCodeTakenCode = "payroll_item_code_taken";

    /// <summary>Referência da linha "Salário" da Mensal (CA 5).</summary>
    public const string MonthlySalaryReference = "30 dias";

    /// <summary>Totais pelas linhas (CA 6, ADR 0019 §9): cada valor em 2 casas (AwayFromZero), a informativa fica fora do líquido.</summary>
    public static PayslipTotals Totals(IEnumerable<PayslipLineValue> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        decimal gross = 0m, deductions = 0m;
        foreach (var line in lines)
        {
            var amount = Round(line.Amount);
            if (line.Type == PayrollItemKind.Earning)
            {
                gross += amount;
            }
            else if (line.Type == PayrollItemKind.Deduction)
            {
                deductions += amount;
            }
        }

        return new PayslipTotals(gross, deductions, gross - deductions);
    }

    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Valor da linha: maior que zero, com até 2 casas (CA 6).</summary>
    public static bool ValidAmount(decimal amount) => amount > 0m && amount <= MaxAmount && decimal.Round(amount, 2) == amount;

    /// <summary>Base ou FGTS do mês: zero ou mais, com até 2 casas.</summary>
    public static bool ValidBase(decimal amount) => amount >= 0m && amount <= MaxAmount && decimal.Round(amount, 2) == amount;

    public static bool ValidReference(string? reference) => (reference?.Trim().Length ?? 0) <= ReferenceMaxLength;

    /// <summary>Código da rubrica: letras, dígitos, "_" ou "-", de 1 a 10, sem começar por símbolo (vem também do CSV).</summary>
    public static string? NormalizeCode(string? code)
    {
        var text = (code ?? string.Empty).Trim().ToUpperInvariant();
        return text.Length is > 0 and <= CodeMaxLength && char.IsAsciiLetterOrDigit(text[0]) && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? text : null;
    }

    /// <summary>Primeiro dia da competência.</summary>
    public static DateOnly PeriodStart(int year, int month) => new(year, month, 1);

    public static DateOnly PeriodEnd(DateOnly period) => period.AddMonths(1).AddDays(-1);

    /// <summary>Competência "2026-10" (CA 4); null se inválida.</summary>
    public static DateOnly? ParsePeriod(string? text) =>
        DateOnly.TryParseExact((text ?? string.Empty).Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year is >= 2000 and <= 2100 ? date : null;

    /// <summary>
    /// Funcionário da competência (CA 4): admitido até o último dia e não desligado antes do primeiro. O afastado entra (o que ele
    /// recebe vem da contadora).
    /// </summary>
    public static bool InPeriod(DateOnly admission, DateOnly? termination, DateOnly period) =>
        admission <= PeriodEnd(period) && (termination is not { } end || end >= period);

    /// <summary>
    /// Vencimento proposto da guia (CA 3): o dia <paramref name="day"/> do mês seguinte à competência (o último dia, se o mês for
    /// mais curto). O dia não útil antecipa pelo calendário (no serviço).
    /// </summary>
    public static DateOnly DueDateNextMonth(DateOnly period, int day)
    {
        var next = period.AddMonths(1);
        return new DateOnly(next.Year, next.Month, Math.Clamp(day, 1, DateTime.DaysInMonth(next.Year, next.Month)));
    }

    /// <summary>Variação do líquido em % contra o mês anterior (CA 11); null sem o anterior ou com o anterior zero.</summary>
    public static decimal? Variation(decimal current, decimal? previous) =>
        previous is { } p && p != 0m ? decimal.Round((current - p) / Math.Abs(p) * 100m, 2, MidpointRounding.AwayFromZero) : null;

    public static bool AboveLimit(decimal? variation, decimal limitPct) => variation is { } v && Math.Abs(v) > limitPct;

    public static string TypeLabel(PayrollRunType type) => type switch
    {
        PayrollRunType.Monthly => "Mensal",
        PayrollRunType.Advance => "Adiantamento",
        PayrollRunType.Vacation => "Férias",
        PayrollRunType.Thirteenth1 => "13º 1ª parcela",
        PayrollRunType.Thirteenth2 => "13º 2ª parcela",
        PayrollRunType.Termination => "Rescisão",
        _ => type.ToString(),
    };

    /// <summary>"outubro de 2026".</summary>
    public static string PeriodLabel(DateOnly period) => period.ToString("MMMM 'de' yyyy", CultureInfo.GetCultureInfo("pt-BR"));

    /// <summary>Férias e Rescisão: quem lança escolhe os funcionários (CA 4).</summary>
    public static bool ChoosesEmployees(PayrollRunType type) => type is PayrollRunType.Vacation or PayrollRunType.Termination;

    public static string ChargeLabel(PayrollChargeKind kind) => kind switch
    {
        PayrollChargeKind.Fgts => "FGTS",
        PayrollChargeKind.Inss => "INSS",
        PayrollChargeKind.Irrf => "IRRF retido",
        PayrollChargeKind.Union => "Contribuição sindical",
        _ => "Outros",
    };

    public static Error Closed(DateOnly period) => Error.Conflict(ClosedCode, $"A folha de {PeriodLabel(period)} está fechada.");

    public static Error Field(string field, string message) =>
        Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });
}
