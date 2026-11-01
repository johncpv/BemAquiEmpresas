using BemAqui.Finance.Contracts;
using BemAqui.HR.Domain;

namespace BemAqui.HR.Application;

/// <summary>Pendência que impede o fechamento (RH-02c CA 1), com o link para resolver.</summary>
public sealed record PayrollCloseIssue(string Code, string Message, string? Link);

/// <summary>Funcionário nos avisos "Novos na folha" e "Conta alterada" (CA 1).</summary>
public sealed record PayrollClosePerson(Guid EmployeeId, string Name, string? Registration, DateTimeOffset? QuarantineUntil);

/// <summary>
/// Prévia do fechamento (CA 1), só pela rota <c>/sensitive</c>: as pendências, a data de pagamento sugerida pelo tipo (editável), os avisos
/// e o impacto ("Gera {n} títulos a pagar de salários, total {valor}, e {m} títulos de guias, total {valor}"). <c>Changes</c>: as mudanças de
/// tabela legal e de parâmetro desde o último fechamento (FLH-01 CA 12), com ciência obrigatória.
/// </summary>
public sealed record PayrollClosePreview(
    Guid RunId,
    DateOnly Period,
    PayrollRunType Type,
    IReadOnlyList<PayrollCloseIssue> Blockers,
    DateOnly? SuggestedPaymentDate,
    bool PaymentDateRequired,
    bool PaymentDateConfirmed,
    IReadOnlyList<PayrollClosePerson> Newcomers,
    IReadOnlyList<PayrollClosePerson> AccountChanged,
    int SalaryCount,
    decimal SalaryTotal,
    int ChargeCount,
    decimal ChargeTotal,
    uint Version,
    IReadOnlyList<PayrollChangeNotice> Changes);

/// <summary>
/// Fechar (CA 2): a data de pagamento (vazia usa a sugerida), o código do 2FA de novo e a versão da folha. Com mudança de tabela ou de
/// parâmetro desde o último fechamento (FLH-01 CA 12), a ciência é obrigatória.
/// </summary>
public sealed record PayrollCloseInput(DateOnly? PaymentDate, string? TwoFactorCode, uint Version, bool AcknowledgedChanges = false);

/// <summary>Folha fechada: o resumo, quantos títulos e a mensagem "Folha de {mês} fechada. {n} títulos a pagar gerados.".</summary>
public sealed record PayrollCloseResult(PayrollRunSummary Run, int Payables, string Message, int ChangedAfterNotice = 0);

/// <summary>Reabrir (CA 13): o motivo (de 5 a 200) e a versão.</summary>
public sealed record PayrollReopenInput(string? Reason, uint Version);

/// <summary>
/// Título da folha na aba Pagamentos (CA 5): número, favorecido, valor, vencimento e a situação (só com <c>finance.view</c>; sem ela, o
/// rótulo é "Título gerado" e <c>Status</c> vem nulo). A conta em quarentena vem marcada, com o fim.
/// </summary>
public sealed record PayrollPaymentRow(
    Guid PayableId, string Number, bool IsSalary, Guid OriginId, Guid? EmployeeId, string PayeeName, decimal Amount, DateOnly? DueDate, PayableStatus? Status, string StatusLabel,
    Guid? PaymentOrderId, bool InQuarantine, DateTimeOffset? QuarantineUntil);

/// <summary>
/// Aba Pagamentos (CA 5): os títulos, os totais e as pré-seleções das OPs dos salários (sem os de conta em quarentena, listados em
/// <c>Quarantined</c>) e das guias; só os títulos ainda em aberto entram na pré-seleção.
/// </summary>
public sealed record PayrollPayments(
    IReadOnlyList<PayrollPaymentRow> Items, decimal SalaryTotal, decimal ChargeTotal, IReadOnlyList<Guid> SalaryPreselected, IReadOnlyList<PayrollPaymentRow> Quarantined,
    IReadOnlyList<Guid> ChargePreselected, bool CanSeeStatus);

/// <summary>Folha prevista recalculada (CA 14): quantas linhas ativas e os meses da janela, sem valores.</summary>
public sealed record PayrollForecastResult(int Lines, int Months, DateOnly? From, DateOnly? To);
