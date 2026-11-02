using BemAqui.HR.Domain;

namespace BemAqui.HR.Application;

/// <summary>
/// "Simular rescisão" e "Calcular rescisão" (CA 1, 3 e 4): o tipo (da matriz), o aviso, a data (na simulação; no cálculo vale a do
/// desligamento registrado), o fim previsto da experiência (arts. 479 e 480) e o saldo do FGTS do extrato com a data dele.
/// </summary>
public sealed record TerminationRequestInput(TerminationKind? Kind, TerminationNoticeKind? NoticeKind, DateOnly? TerminationDate, DateOnly? ContractEndDate, decimal? FgtsBalance, DateOnly? FgtsStatementDate);

/// <summary>Verba ou desconto da rescisão com a memória (CA 1 e 3).</summary>
public sealed record TerminationLineView(string Code, string Name, PayrollItemKind Type, string? Reference, decimal Amount, IReadOnlyList<string> Memory);

/// <summary>
/// O resultado da rescisão (CA 1): as verbas e os descontos com a memória, o líquido, o FGTS do mês e o rescisório, a multa a recolher, os
/// encargos e o custo total para a empresa, o prazo do pagamento e os avisos do cálculo. Só com <c>hr.view_sensitive</c>.
/// </summary>
public sealed record TerminationResultView(
    TerminationKind Kind,
    string KindLabel,
    TerminationNoticeKind NoticeKind,
    DateOnly TerminationDate,
    int NoticeDays,
    decimal PaidNoticeDays,
    DateOnly ProjectedEnd,
    IReadOnlyList<TerminationLineView> Lines,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    decimal FgtsMonth,
    decimal FgtsTermination,
    decimal FinePct,
    decimal Fine,
    IReadOnlyList<string> FineMemory,
    decimal EmployerCharges,
    decimal TotalCost,
    DateOnly PaymentDeadline,
    IReadOnlyList<string> Warnings,
    string CalculatorVersion);

/// <summary>Um tipo de desligamento para escolher (CA 1, Propositivo): o aviso mais comum, se pede o fim da experiência e se tem multa (saldo do FGTS obrigatório).</summary>
public sealed record TerminationKindOption(TerminationKind Kind, string Label, TerminationNoticeKind DefaultNotice, bool NeedsContractEnd, decimal FgtsFinePct);

/// <summary>
/// A rescisão do funcionário (sem valor, salvo o saldo do FGTS com <c>hr.view_sensitive</c>): tipo, aviso, datas, a folha de rescisão, a
/// situação e o prazo do pagamento.
/// </summary>
public sealed record TerminationItem(
    Guid Id,
    TerminationKind Kind,
    string KindLabel,
    TerminationNoticeKind NoticeKind,
    string NoticeLabel,
    int NoticeDays,
    DateOnly TerminationDate,
    DateOnly? ContractEndDate,
    decimal? FgtsBalance,
    DateOnly? FgtsStatementDate,
    TerminationStatus Status,
    string StatusLabel,
    Guid? PayrollRunId,
    Guid? PayslipId,
    bool IsSimulation,
    bool RunClosed,
    DateOnly PaymentDeadline,
    DateTimeOffset? CalculatedAt,
    uint Version);

/// <summary>
/// Aba "Rescisão" do funcionário (CA 1, 3 e 10): a data do desligamento (CAD-04b), a rescisão (ou nula), os tipos da matriz e os padrões
/// (a data de hoje, os dias do aviso e o prazo), e o próximo passo.
/// </summary>
public sealed record TerminationView(
    Guid EmployeeId,
    string EmployeeName,
    bool Terminated,
    DateOnly? TerminationDate,
    TerminationItem? Termination,
    IReadOnlyList<TerminationKindOption> Kinds,
    DateOnly SuggestedDate,
    int NoticeDays,
    int PaymentDays,
    IReadOnlyList<PayrollNextStep> NextSteps);

/// <summary>"Rescisão calculada" (CA 3): a rescisão, a folha, o holerite, o resultado, a mensagem e os próximos passos.</summary>
public sealed record TerminationCalculationResult(TerminationItem Termination, PayrollRunSummary Run, Guid PayslipId, TerminationResultView Result, string Message, IReadOnlyList<PayrollNextStep> NextSteps);

/// <summary>Estornar o desligamento (CA 10): o motivo (5 a 200) e a versão da rescisão.</summary>
public sealed record TerminationCancelInput(string? Reason, uint Version);
