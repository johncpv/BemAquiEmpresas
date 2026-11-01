using BemAqui.HR.Domain;

namespace BemAqui.HR.Application;

/// <summary>Competência sugerida (Propositivo): a do último espelho fechado, "2026-10"; nula sem nenhum.</summary>
public sealed record PayrollSuggestedPeriod(string? Period);

/// <summary>"Calcular" e "Recalcular" (FLH-02a CA 1 e 13): o resumo, se foi para o segundo plano (mais de 20 funcionários) e a mensagem.</summary>
public sealed record PayrollCalculationStart(PayrollRunSummary Run, bool Queued, int Employees, string Message);

/// <summary>Versão de tabela usada no cálculo (ADR 0039 §2), com o rótulo "INSS de janeiro/2026" e a situação.</summary>
public sealed record PayrollCalcTableRef(Guid Id, PayrollTaxTableKind Kind, string Label, DateOnly ValidFrom, PayrollTaxTableStatus Status);

/// <summary>Próximo passo propositivo depois do cálculo (CA 15): "Comparar com a lançada" antes da virada, "Fechar folha" depois.</summary>
public sealed record PayrollNextStep(string Label, string Link);

/// <summary>
/// Situação do cálculo da folha (CA 1, 14 e 15), sem valor (sai com <c>hr.view</c>): calculando, simulação, tabelas a confirmar, quantos
/// funcionários com cadastro ou tabela mudados depois do cálculo e por quê, avisos por funcionário e o próximo passo.
/// </summary>
public sealed record PayrollCalculationStatus(
    Guid RunId,
    bool Calculated,
    bool Calculating,
    DateTimeOffset? CalculatingSince,
    bool IsSimulation,
    bool Official,
    string? CalculatorVersion,
    DateTimeOffset? CalculatedAt,
    IReadOnlyList<PayrollCalcTableRef> Tables,
    IReadOnlyList<string> UnconfirmedTables,
    int StaleCount,
    string? StaleMessage,
    IReadOnlyList<string> StaleReasons,
    IReadOnlyList<PayrollCalcEmployeeIssue> Issues,
    Guid? RectifiesRunId,
    IReadOnlyList<Guid> Rectifications,
    PayrollNextStep? NextStep);

/// <summary>Aviso ou erro de um funcionário (sem valor): "Sem jornada", "documentos do salário-família pendentes".</summary>
public sealed record PayrollCalcEmployeeIssue(Guid EmployeeId, string EmployeeName, string Message, bool Blocking);

/// <summary>Lançamento avulso (CA 13): rubrica sem regra, referência, valor e o motivo obrigatório (5 a 200).</summary>
public sealed record PayrollManualLineInput(Guid? PayrollItemId, string? Reference, decimal Amount, string? Note);

/// <summary>Entrada do retrato mostrada na memória (CA 12): só o do próprio funcionário, sem tipo de afastamento.</summary>
public sealed record PayrollCalcInputView(string Label, string Value);

/// <summary>
/// O cálculo do holerite (CA 12): versão do motor, quando, as tabelas e os parâmetros usados, as entradas do retrato, os avisos e os
/// encargos da empresa com a memória. Só pela rota <c>/sensitive</c> do holerite.
/// </summary>
public sealed record PayslipCalculationInfo(
    string? CalculatorVersion,
    DateTimeOffset? CalculatedAt,
    bool IsSimulation,
    IReadOnlyList<PayrollCalcTableRef> Tables,
    IReadOnlyList<PayrollCalcInputView> Parameters,
    IReadOnlyList<PayrollCalcInputView> Inputs,
    IReadOnlyList<string> Issues,
    decimal EmployerCharges,
    IReadOnlyList<string> EmployerChargesMemory);

/// <summary>Linha de "Ver diferença" (CA 14): a rubrica, o valor fechado e o que o cálculo de hoje daria.</summary>
public sealed record PayrollDifferenceLine(Guid PayrollItemId, string Code, string Name, PayrollItemKind Kind, decimal Before, decimal After);

public sealed record PayrollDifferenceEmployee(Guid EmployeeId, string EmployeeName, string? Registration, decimal NetBefore, decimal NetAfter, IReadOnlyList<PayrollDifferenceLine> Lines);

/// <summary>"Ver diferença" (CA 14): o que mudaria por funcionário e rubrica, sem recalcular a folha fechada. Só por <c>/sensitive</c>.</summary>
public sealed record PayrollRunDifference(Guid RunId, IReadOnlyList<PayrollDifferenceEmployee> Employees, decimal NetBefore, decimal NetAfter, IReadOnlyList<string> Reasons);

/// <summary>Entrada do histórico do cálculo (CA 19), sem valor: quem, quando, o quê, a versão do motor, as tabelas e os parâmetros.</summary>
public sealed record PayrollCalculationEntry(
    Guid Id,
    PayrollCalculationAction Action,
    string ActionLabel,
    DateTimeOffset At,
    string? ByName,
    string CalculatorVersion,
    IReadOnlyList<PayrollCalcTableRef> Tables,
    IReadOnlyList<string> Parameters,
    int Employees);

/// <summary>Os totais antes e depois de cada entrada do histórico (CA 19): só com <c>hr.view_sensitive</c>, pela rota <c>/sensitive</c>.</summary>
public sealed record PayrollCalculationTotals(Guid Id, decimal? GrossBefore, decimal? NetBefore, decimal GrossAfter, decimal NetAfter);
