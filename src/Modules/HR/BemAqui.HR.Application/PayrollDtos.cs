using BemAqui.HR.Domain;

namespace BemAqui.HR.Application;

/// <summary>
/// Resumo da folha (RH-02a CA 12): competência, tipo, modo, situação, número de holerites e quem fechou. Nenhum valor: sai para quem
/// tem só <c>hr.view</c> (o teste de arquitetura confere).
/// </summary>
public sealed record PayrollRunSummary(
    Guid Id,
    DateOnly Period,
    PayrollRunType Type,
    PayrollRunMode Mode,
    PayrollRunStatus Status,
    int PayslipCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    string? ClosedByName,
    uint Version);

/// <summary>Totais da folha (só com <c>hr.view_sensitive</c>, rota <c>/sensitive</c>): o líquido total na lista.</summary>
public sealed record PayrollRunTotals(Guid RunId, decimal Gross, decimal Deductions, decimal Net);

/// <summary>Nova folha (CA 4): competência "2026-10", tipo e, em Férias e Rescisão, os funcionários escolhidos.</summary>
public sealed record PayrollRunCreateInput(string? Period, PayrollRunType? Type, IReadOnlyList<Guid>? EmployeeIds);

/// <summary>Funcionário da competência, para escolher em Férias e Rescisão (sem dado sensível).</summary>
public sealed record PayrollEligibleEmployee(Guid Id, string Name, string? Registration, DateOnly AdmissionDate, DateOnly? TerminationDate);

/// <summary>Holerite na lista da folha (sensível): totais e o número de linhas.</summary>
public sealed record PayslipRow(Guid Id, Guid EmployeeId, string EmployeeName, string? Registration, decimal Gross, decimal Deductions, decimal Net, int LineCount, uint Version);

public sealed record PayslipLineDetail(Guid PayrollItemId, string Code, string Description, PayrollItemKind Type, string? Reference, decimal Amount);

/// <summary>Holerite (CA 5 e 6), só pela rota <c>/sensitive</c>, com <c>SensitiveRead</c>.</summary>
public sealed record PayslipDetail(
    Guid Id,
    Guid PayrollRunId,
    Guid EmployeeId,
    string EmployeeName,
    string? Registration,
    IReadOnlyList<PayslipLineDetail> Lines,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    decimal InssBase,
    decimal FgtsBase,
    decimal IrrfBase,
    decimal FgtsAmount,
    bool Editable,
    uint Version);

public sealed record PayslipLineInput(Guid? PayrollItemId, string? Reference, decimal Amount);

/// <summary>Salvar o holerite (CA 6): as linhas substituem as de antes; bases e FGTS do mês; a versão (<c>xmin</c>, CA 15).</summary>
public sealed record PayslipInput(IReadOnlyList<PayslipLineInput>? Lines, decimal InssBase, decimal FgtsBase, decimal IrrfBase, decimal FgtsAmount, uint Version);

/// <summary>"Copiar do mês anterior" (CA 5): o holerite anterior do mesmo tipo, para entrar como rascunho editável; vazio sem anterior.</summary>
public sealed record PayslipPrevious(DateOnly? Period, IReadOnlyList<PayslipLineDetail> Lines, decimal InssBase, decimal FgtsBase, decimal IrrfBase, decimal FgtsAmount);

public sealed record PayrollChargeDetail(Guid Id, PayrollChargeKind Kind, string Description, decimal Amount, DateOnly DueDate, Guid? PayeePartyId, string? PayeeName, uint Version);

/// <summary>
/// Guias da competência (CA 7): as linhas, os padrões (vencimento e órgão) de cada tipo, a soma do FGTS dos holerites e o aviso
/// quando ela difere da guia do FGTS.
/// </summary>
public sealed record PayrollChargesView(IReadOnlyList<PayrollChargeDetail> Items, IReadOnlyList<PayrollChargeDefault> Defaults, decimal PayslipsFgts, decimal? FgtsCharges, bool FgtsMismatch, bool Editable);

/// <summary>Guia (CA 7): vencimento e órgão vazios usam o padrão do CA 3.</summary>
public sealed record PayrollChargeInput(PayrollChargeKind? Kind, string? Description, decimal Amount, DateOnly? DueDate, Guid? PayeePartyId, uint? Version);

public sealed record PayrollImportItemTotal(string Code, string Name, PayrollItemKind Kind, int Lines, decimal Total);

/// <summary>
/// Prévia da importação (CA 8): funcionários encontrados, linhas por rubrica, totais e os erros por linha (até 100 na lista, o total em
/// <c>ErrorCount</c>). Só com zero erro <c>CanImport</c>.
/// </summary>
public sealed record PayrollImportPreview(int Employees, IReadOnlyList<string> EmployeeNames, int Lines, IReadOnlyList<PayrollImportItemTotal> ByItem, decimal Gross, decimal Deductions, decimal Net,
    IReadOnlyList<string> Errors, int ErrorCount, bool CanImport);

public sealed record PayrollImportResult(int Lines, int Employees);

public sealed record PayrollReviewPayslip(Guid PayslipId, string EmployeeName, decimal Amount);

/// <summary>Total por rubrica na conferência (CA 11), com os holerites que o compõem (a soma é o mesmo total).</summary>
public sealed record PayrollReviewItem(Guid PayrollItemId, string Code, string Name, PayrollItemKind Kind, decimal Total, IReadOnlyList<PayrollReviewPayslip> Payslips);

public sealed record PayrollReviewVariation(Guid PayslipId, Guid EmployeeId, string EmployeeName, decimal Net, decimal? PreviousNet, decimal? VariationPct, bool Alert);

public sealed record PayrollReviewEmployee(Guid PayslipId, Guid EmployeeId, string EmployeeName, DateTimeOffset? QuarantineUntil);

/// <summary>Conferência da folha (CA 11), só pela rota <c>/sensitive</c>.</summary>
public sealed record PayrollReview(
    int PayslipCount,
    decimal Gross,
    decimal Deductions,
    decimal Net,
    decimal AlertPct,
    DateOnly? PreviousPeriod,
    IReadOnlyList<PayrollReviewItem> ByItem,
    IReadOnlyList<PayrollReviewVariation> Variations,
    IReadOnlyList<PayrollReviewEmployee> Empty,
    IReadOnlyList<PayrollReviewEmployee> Newcomers,
    IReadOnlyList<PayrollReviewEmployee> AccountChanged);
