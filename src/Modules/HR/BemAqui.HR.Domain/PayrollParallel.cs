using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.HR.Domain;

/// <summary>Tipo da folha na conferência paralela e na virada (FLH-06): o 13º junta as duas parcelas; o adiantamento segue a Mensal.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollParallelType>))]
public enum PayrollParallelType
{
    Monthly,
    Vacation,
    Thirteenth,
    Termination,
}

/// <summary>Situação da competência na conferência (CA 3): em conferência (sem diferença, pronta para aceitar), aceita ou reprovada.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollParallelStatus>))]
public enum PayrollParallelStatus
{
    Open,
    Accepted,
    Rejected,
}

/// <summary>Classificação da diferença (CA 2).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PayrollDifferenceClassification>))]
public enum PayrollDifferenceClassification
{
    SystemError,
    AccountantError,
    RuleDifference,
    Rounding,
}

/// <summary>
/// Competência em conferência paralela (<c>hr.parallel_runs</c>, FLH-06, ADR 0039 §5): a folha lançada (oficial, da contadora) e a calculada
/// (simulação) do mesmo tipo e competência, a situação e, no aceite, quem, quando, a contadora responsável (nome e CRC) e o termo assinado
/// (anexo <c>PayrollParallelTerm</c>). Os totais do líquido são dado da folha (<c>[PersonalData]</c>).
/// </summary>
[DocumentName("A conferência")]
public sealed class PayrollParallelRun : Entity, IAuditable, IConcurrencyVersioned
{
    public DateOnly Period { get; init; }

    public PayrollParallelType RunType { get; init; }

    /// <summary>O tipo exato das folhas comparadas (Thirteenth1 ou Thirteenth2 no 13º).</summary>
    public PayrollRunType PayrollType { get; init; }

    public Guid EnteredRunId { get; set; }

    public Guid CalculatedRunId { get; set; }

    public PayrollParallelStatus Status { get; set; } = PayrollParallelStatus.Open;

    public DateTimeOffset ComparedAt { get; set; }

    public Guid? ComparedBy { get; set; }

    /// <summary>Quantas linhas (funcionário e rubrica) diferentes na última comparação.</summary>
    public int Differences { get; set; }

    public int Employees { get; set; }

    [PersonalData]
    public decimal EnteredNet { get; set; }

    [PersonalData]
    public decimal CalculatedNet { get; set; }

    public Guid? AcceptedBy { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public string? AccountantName { get; set; }

    public string? AccountantCrc { get; set; }

    public Guid? SignedTermAttachmentId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }
}

/// <summary>
/// Linha da comparação (<c>hr.parallel_differences</c>, CA 1 e 2): funcionário e rubrica, o valor da contadora, o do sistema, a diferença em R$ e
/// em % (sobre o da contadora, 4 casas; nulo quando ele é zero), a classificação e a justificativa, e o caso de correção anonimizado do
/// "Erro do sistema". Valores por funcionário: <c>[PersonalData]</c> (LGPD-01a), fora do log.
/// </summary>
public sealed class PayrollParallelDifference : Entity, IAuditable
{
    public Guid ParallelRunId { get; init; }

    public Guid EmployeeId { get; init; }

    public string PayrollItemCode { get; init; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;

    public PayrollItemKind Kind { get; set; }

    [PersonalData]
    public decimal EnteredAmount { get; set; }

    [PersonalData]
    public decimal CalculatedAmount { get; set; }

    [PersonalData]
    public decimal Difference { get; set; }

    [PersonalData]
    public decimal? DifferencePct { get; set; }

    public PayrollDifferenceClassification? Classification { get; set; }

    /// <summary>Texto livre do RH.</summary>
    [PersonalData]
    public string? Justification { get; set; }

    public Guid? ClassifiedBy { get; set; }

    public DateTimeOffset? ClassifiedAt { get; set; }

    /// <summary>Caso de correção (jsonb, CA 2): rubricas, bases, parâmetros e entradas, sem nome, CPF, matrícula nem data de nascimento.</summary>
    [PersonalData]
    public string? CorrectionCase { get; set; }
}

/// <summary>De-para de rubricas (<c>hr.payroll_item_mappings</c>, CA 1): o código da folha da contadora que corresponde ao código do sistema.</summary>
public sealed class PayrollItemMapping : Entity, IAuditable
{
    public string EnteredCode { get; init; } = string.Empty;

    public string CalculatedCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Virada e volta da folha (<c>hr.payroll_mode_changes</c>, CA 5 a 7): o tipo, o modo que passou a valer, a partir de qual competência, quem,
/// quando, o motivo (da volta), as competências aceitas que sustentaram a virada e a contadora. Só cresce.
/// </summary>
public sealed class PayrollModeChange : Entity, IAuditable
{
    public PayrollParallelType RunType { get; init; }

    public PayrollRunMode Mode { get; init; }

    public DateOnly FromPeriod { get; init; }

    public DateTimeOffset ChangedAt { get; init; }

    public Guid? ChangedBy { get; init; }

    /// <summary>Motivo da volta para a lançada (decisão sobre a folha, não sobre um funcionário).</summary>
    public string? Reason { get; init; }

    public List<Guid> ParallelRunIds { get; init; } = [];

    public string? AccountantName { get; init; }

    public string? AccountantCrc { get; init; }
}

/// <summary>Linha de uma folha para a comparação: funcionário, código, nome e tipo da rubrica e o valor (somado por código).</summary>
public sealed record PayrollParallelLine(Guid EmployeeId, string Code, string Name, PayrollItemKind Kind, decimal Amount);

/// <summary>Resultado da comparação por funcionário e rubrica (CA 1): tolerância zero.</summary>
public sealed record PayrollParallelRow(Guid EmployeeId, string Code, string Name, PayrollItemKind Kind, decimal Entered, decimal Calculated)
{
    public decimal Difference => Calculated - Entered;

    public decimal? DifferencePct => PayrollParallelRules.Pct(Entered, Difference);

    public bool Equal => Difference == 0m;
}

/// <summary>Uma competência para a contagem das seguidas (CA 5).</summary>
public sealed record PayrollParallelMonth(DateOnly Period, PayrollParallelStatus Status, DateTimeOffset? AcceptedAt);

/// <summary>Linha do caso de correção (CA 2): só a rubrica e os valores, com a memória de cálculo do sistema.</summary>
public sealed record PayrollCorrectionLine(string Code, string Name, PayrollItemKind Kind, decimal Entered, decimal Calculated, decimal Difference, IReadOnlyList<string> Memory);

/// <summary>
/// Caso de correção anonimizado (CA 2, ADR 0039 §4): competência, versão do motor, tabelas, o retrato das entradas sem dado que identifica
/// (o nascimento do dependente vira a idade, o motivo do avulso e da decisão do banco somem) e as rubricas com a diferença. Vira caso do
/// dataset sintético do CI.
/// </summary>
public sealed record PayrollCorrectionCase(
    int Version,
    string Period,
    string? CalculatorVersion,
    IReadOnlyList<Guid> TaxTableIds,
    PayrollCalcSnapshot? Inputs,
    IReadOnlyList<PayrollCorrectionLine> Lines);

/// <summary>Regras da conferência paralela e da virada (FLH-06), sem banco.</summary>
public static class PayrollParallelRules
{
    public const int DefaultMonths = 3;
    public const int MaxMonths = 12;
    public const int JustificationMin = 5;
    public const int JustificationMax = 500;
    public const int AccountantNameMax = 120;
    public const int CrcMax = 30;
    public const int ReasonMin = 10;
    public const int ReasonMax = 500;

    public const string CalculatedMissingCode = "parallel_calculated_missing";
    public const string EnteredMissingCode = "parallel_entered_missing";
    public const string AcceptedCode = "parallel_run_accepted";
    public const string DifferencesCode = "parallel_has_differences";
    public const string ChangedCode = "parallel_changed";
    public const string TermMissingCode = "parallel_term_missing";
    public const string TermScanningCode = "parallel_term_scanning";
    public const string NotEnoughMonthsCode = "parallel_not_enough_months";
    public const string AlreadyCalculatedCode = "payroll_mode_already_calculated";
    public const string NotCalculatedCode = "payroll_mode_not_calculated";
    public const string NotSupportedCode = "parallel_type_not_supported";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>O tipo da conferência pela folha: o 13º junta as parcelas; o adiantamento não é conferido (null).</summary>
    public static PayrollParallelType? TypeOf(PayrollRunType type) => type switch
    {
        PayrollRunType.Monthly => PayrollParallelType.Monthly,
        PayrollRunType.Vacation => PayrollParallelType.Vacation,
        PayrollRunType.Thirteenth1 or PayrollRunType.Thirteenth2 => PayrollParallelType.Thirteenth,
        PayrollRunType.Termination => PayrollParallelType.Termination,
        _ => null,
    };

    public static string TypeLabel(PayrollParallelType type) => type switch
    {
        PayrollParallelType.Monthly => "Mensal",
        PayrollParallelType.Vacation => "Férias",
        PayrollParallelType.Thirteenth => "13º salário",
        _ => "Rescisão",
    };

    public static string ClassificationLabel(PayrollDifferenceClassification classification) => classification switch
    {
        PayrollDifferenceClassification.SystemError => "Erro do sistema",
        PayrollDifferenceClassification.AccountantError => "Erro da contadora",
        PayrollDifferenceClassification.RuleDifference => "Regra diferente: ajustar parâmetro",
        _ => "Arredondamento",
    };

    public static string StatusLabel(PayrollParallelStatus status) => status switch
    {
        PayrollParallelStatus.Open => "Em conferência",
        PayrollParallelStatus.Accepted => "Aceita",
        _ => "Reprovada",
    };

    /// <summary>Diferença em % sobre o valor da contadora, com 4 casas (CA 1); null quando o valor da contadora é zero ("—").</summary>
    public static decimal? Pct(decimal entered, decimal difference) =>
        entered == 0m ? null : decimal.Round(difference / Math.Abs(entered) * 100m, 4, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Compara por funcionário e rubrica (CA 1): as linhas somadas por código; o código da contadora passa pelo de-para quando difere do
    /// sistema. Funcionário ou rubrica de um lado só entra com zero do outro. Nome e tipo vêm do sistema (ou da contadora, se só ela tem).
    /// </summary>
    public static IReadOnlyList<PayrollParallelRow> Compare(IEnumerable<PayrollParallelLine> entered, IEnumerable<PayrollParallelLine> calculated, IReadOnlyDictionary<string, string>? mappings = null)
    {
        ArgumentNullException.ThrowIfNull(entered);
        ArgumentNullException.ThrowIfNull(calculated);
        string Map(string code) => mappings is not null && mappings.TryGetValue(code, out var to) ? to : code;
        var left = entered.GroupBy(l => (l.EmployeeId, Code: Map(l.Code))).ToDictionary(g => g.Key, g => (g.First().Name, g.First().Kind, Amount: g.Sum(x => x.Amount)));
        var right = calculated.GroupBy(l => (l.EmployeeId, l.Code)).ToDictionary(g => g.Key, g => (g.First().Name, g.First().Kind, Amount: g.Sum(x => x.Amount)));
        return left.Keys.Union(right.Keys).Select(key =>
        {
            var has = right.TryGetValue(key, out var r);
            var l = left.GetValueOrDefault(key);
            return new PayrollParallelRow(key.EmployeeId, key.Code, has ? r.Name : l.Name, has ? r.Kind : l.Kind, l.Amount, has ? r.Amount : 0m);
        }).OrderBy(r => r.EmployeeId).ThenBy(r => r.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>Líquido de um lado (proventos − descontos; a informativa fica fora).</summary>
    public static decimal Net(IEnumerable<PayrollParallelRow> rows, bool calculated) =>
        rows.Sum(r => r.Kind switch
        {
            PayrollItemKind.Earning => calculated ? r.Calculated : r.Entered,
            PayrollItemKind.Deduction => -(calculated ? r.Calculated : r.Entered),
            _ => 0m,
        });

    /// <summary>Aceite (CA 3): diferença zero em cada rubrica de todos os funcionários (e, com isso, no líquido).</summary>
    public static bool CanAccept(IEnumerable<PayrollParallelRow> rows) => rows.All(r => r.Equal);

    /// <summary>
    /// Competências seguidas aceitas (CA 5 e 7), da mais recente para trás: a que está em conferência no topo não conta nem quebra; a
    /// reprovada, a lacuna de mês e a aceita antes da última volta para a lançada (<paramref name="since"/>) quebram a contagem.
    /// </summary>
    public static IReadOnlyList<DateOnly> ConsecutiveAccepted(IEnumerable<PayrollParallelMonth> months, DateTimeOffset? since = null)
    {
        ArgumentNullException.ThrowIfNull(months);
        var ordered = months.OrderByDescending(m => m.Period).ToList();
        var result = new List<DateOnly>();
        var i = 0;
        while (i < ordered.Count && ordered[i].Status == PayrollParallelStatus.Open)
        {
            i++;
        }

        for (; i < ordered.Count; i++)
        {
            var m = ordered[i];
            var counts = m.Status == PayrollParallelStatus.Accepted && (since is null || m.AcceptedAt > since);
            if (!counts || (result.Count > 0 && m.Period != result[^1].AddMonths(-1)))
            {
                break;
            }

            result.Add(m.Period);
        }

        return result;
    }

    /// <summary>A competência seguinte à mais recente aceita: dali em diante a calculada é a oficial (CA 5).</summary>
    public static DateOnly FromPeriod(IReadOnlyList<DateOnly> accepted) => accepted.Max().AddMonths(1);

    /// <summary>"2026-05".</summary>
    public static string Month(DateOnly period) => period.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static DateOnly? ParseMonth(string? text) => PayrollRules.ParsePeriod(text);

    /// <summary>
    /// Retrato sem o que identifica (CA 2 e 9): o nascimento do dependente vira 1º de janeiro do ano que dá a mesma idade no fim da
    /// competência, o motivo do avulso e o da decisão do banco de horas somem. Nome, CPF e matrícula nunca estão no retrato.
    /// </summary>
    public static PayrollCalcSnapshot Anonymize(PayrollCalcSnapshot snapshot, DateOnly period)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var end = PayrollRules.PeriodEnd(period);
        return snapshot with
        {
            Dependents = snapshot.Dependents.Select(d => d with { BirthDate = new DateOnly(Math.Max(end.Year - Age(d.BirthDate, end), 1), 1, 1) }).ToList(),
            Manual = snapshot.Manual.Select(m => m with { Note = string.Empty }).ToList(),
            HourBank = snapshot.HourBank is { } bank ? bank with { DecisionNote = null } : null,
        };
    }

    public static string WriteCase(PayrollCorrectionCase correction) => JsonSerializer.Serialize(correction, Json);

    public static PayrollCorrectionCase? ReadCase(string? json) => string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<PayrollCorrectionCase>(json, Json);

    private static int Age(DateOnly birth, DateOnly at)
    {
        var age = at.Year - birth.Year;
        return birth.AddYears(age) > at ? age - 1 : age;
    }
}
