using BemAqui.HR.Domain;

namespace BemAqui.HR.Application;

/// <summary>"Comparar" (CA 1): a competência ("2026-05") e o tipo da folha (padrão Mensal).</summary>
public sealed record PayrollParallelCompareInput(string? Period, PayrollRunType? Type);

/// <summary>
/// Competência na lista da conferência (sai com <c>hr.view</c>, sem valor): situação, quantos funcionários e diferenças, quem comparou e
/// aceitou, a contadora, se tem o termo e se conta para a virada.
/// </summary>
public sealed record PayrollParallelMonthRow(
    Guid Id,
    DateOnly Period,
    PayrollParallelType RunType,
    PayrollRunType PayrollType,
    PayrollParallelStatus Status,
    string StatusLabel,
    int Employees,
    int Differences,
    DateTimeOffset ComparedAt,
    string? ComparedByName,
    DateTimeOffset? AcceptedAt,
    string? AcceptedByName,
    string? AccountantName,
    string? AccountantCrc,
    bool HasSignedTerm,
    bool CountsForSwitch,
    uint Version);

/// <summary>Competência com folha mas ainda sem comparação (Propositivo): o que falta e o link para resolver.</summary>
public sealed record PayrollParallelPending(DateOnly Period, PayrollRunType Type, Guid? CalculatedRunId, Guid? EnteredRunId, bool CanCompare, string Message, string? ActionLabel, string? Link);

/// <summary>Situação de um tipo (CA 6): modo oficial, desde quando e a partir de qual competência, e o caso conferido.</summary>
public sealed record PayrollModeView(PayrollParallelType Type, string Label, PayrollRunMode Mode, DateOnly? FromPeriod, DateTimeOffset? Since, string Situation, DateOnly? AcceptedCase);

/// <summary>
/// RH → Conferência da folha (CA 1, 5 e 6), sem valor: as competências, as pendentes, "{n} de {exigidas} competências seguidas aceitas",
/// se a virada está liberada e a partir de quando, a situação por tipo e se quem vê é do Proprietário.
/// </summary>
public sealed record PayrollParallelOverview(
    IReadOnlyList<PayrollParallelMonthRow> Runs,
    IReadOnlyList<PayrollParallelPending> Pending,
    int Required,
    IReadOnlyList<DateOnly> Consecutive,
    bool CanSwitch,
    DateOnly? SwitchFrom,
    IReadOnlyList<PayrollModeView> Modes,
    bool IsOwner,
    string? LastAccountantName,
    string? LastAccountantCrc);

/// <summary>Célula da tabela lado a lado (CA 1 e 2): os dois valores, a diferença em R$ e em %, a classificação e a memória do sistema.</summary>
public sealed record PayrollParallelCell(
    Guid Id,
    string Code,
    string Name,
    PayrollItemKind Kind,
    decimal Entered,
    decimal Calculated,
    decimal Difference,
    decimal? DifferencePct,
    bool Equal,
    PayrollDifferenceClassification? Classification,
    string? ClassificationLabel,
    string? Justification,
    string? ClassifiedByName,
    DateTimeOffset? ClassifiedAt,
    bool HasCorrectionCase,
    IReadOnlyList<string> Memory);

/// <summary>Funcionário na conferência: o líquido de cada lado, a diferença do líquido e as rubricas (as diferentes primeiro).</summary>
public sealed record PayrollParallelEmployee(Guid EmployeeId, string Name, string? Registration, decimal EnteredNet, decimal CalculatedNet, decimal NetDifference, int Differences, IReadOnlyList<PayrollParallelCell> Rows);

/// <summary>Rubrica com diferença (Propositivo): quantos funcionários e o total da diferença, das que mais diferem para as que menos.</summary>
public sealed record PayrollParallelItemSummary(string Code, string Name, int Differences, decimal TotalDifference);

/// <summary>Detalhe da competência (só por <c>/sensitive</c>, leitura auditada).</summary>
public sealed record PayrollParallelDetail(
    PayrollParallelMonthRow Run,
    Guid EnteredRunId,
    Guid CalculatedRunId,
    decimal EnteredNet,
    decimal CalculatedNet,
    decimal NetDifference,
    bool CanAccept,
    IReadOnlyList<PayrollParallelEmployee> Employees,
    IReadOnlyList<PayrollParallelItemSummary> Items,
    PayrollNextStep? NextStep);

/// <summary>Classificar e justificar a diferença (CA 2).</summary>
public sealed record PayrollDifferenceInput(PayrollDifferenceClassification? Classification, string? Justification);

/// <summary>A célula gravada e o próximo passo da classificação ("Ajustar parâmetro", "Importar a folha corrigida").</summary>
public sealed record PayrollDifferenceResult(PayrollParallelCell Cell, PayrollNextStep? NextStep);

/// <summary>"Aceitar competência" (CA 3): a contadora responsável (nome e CRC), o termo assinado anexado e a versão da conferência.</summary>
public sealed record PayrollParallelAcceptInput(string? AccountantName, string? AccountantCrc, Guid? SignedTermAttachmentId, uint Version);

/// <summary>"Virar para a folha calculada" (CA 5): a confirmação explícita do diálogo com o impacto.</summary>
public sealed record PayrollModeSwitchInput(bool Confirmed);

/// <summary>"Voltar para a folha lançada" (CA 7): o tipo (nulo ou Mensal: todos) e o motivo.</summary>
public sealed record PayrollModeRevertInput(PayrollParallelType? Type, string? Reason);

/// <summary>Resultado da virada ou da volta: a mensagem com o verbo e a situação atualizada.</summary>
public sealed record PayrollModeResult(string Message, PayrollParallelOverview Overview);

/// <summary>De-para de rubricas (CA 1).</summary>
public sealed record PayrollItemMappingRow(Guid Id, string EnteredCode, string CalculatedCode, string? CalculatedName);

public sealed record PayrollItemMappingInput(string? EnteredCode, string? CalculatedCode);

/// <summary>Histórico da conferência (CA 10): quem, quando, o quê; a justificativa só com <c>hr.view_sensitive</c>.</summary>
public sealed record PayrollParallelHistoryEntry(DateTimeOffset At, string? UserName, string Action, string Description, string? Detail);

/// <summary>Arquivo gerado (termo em PDF, casos de correção em JSON), nunca guardado.</summary>
public sealed record PayrollParallelFile(byte[] Content, string FileName, string ContentType);
