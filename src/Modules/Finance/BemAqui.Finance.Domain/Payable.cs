using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Título a pagar (<c>finance.payables</c>, FIN-02, R-FIN-4). Só a OP aprovada paga (FIN-03a); nesta story o título nasce
/// <c>Planned</c> ou <c>Open</c> e pode ser cancelado. Valor líquido = valor − retenções; a OP paga o líquido.
/// </summary>
[DocumentName("O título")]
public sealed class Payable : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; set; } = string.Empty;

    public Guid PartyId { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Conta do plano de contas (Despesa, Ativo ou Passivo, analítica).</summary>
    public Guid AccountId { get; set; }

    public Guid? CostCenterId { get; set; }

    public Guid? ProjectId { get; set; }

    public decimal Amount { get; set; }

    public decimal RetentionIss { get; set; }

    public decimal RetentionInss { get; set; }

    public decimal RetentionIr { get; set; }

    /// <summary>PIS, COFINS e CSLL retidos juntos.</summary>
    public decimal RetentionPcc { get; set; }

    /// <summary>Pago pela OP (FIN-03a), contra o líquido.</summary>
    public decimal PaidAmount { get; set; }

    public DateOnly IssueDate { get; set; }

    /// <summary>Nulo em <see cref="PayableStatus.Planned"/> sem data estimada.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Vencimento estimado (ex.: OS de instalação ainda em andamento, ADR 0019 §17).</summary>
    public bool IsEstimate { get; set; }

    public PayableStatus Status { get; set; } = PayableStatus.Open;

    public DocumentOrigin Origin { get; set; } = DocumentOrigin.Manual;

    public Guid? OriginId { get; set; }

    public Guid? RecurringExpenseId { get; set; }

    /// <summary>Competência da recorrente (primeiro dia do mês).</summary>
    public DateOnly? Competence { get; set; }

    public Guid? InstallmentGroupId { get; set; }

    public PaymentMethodKind? PaymentMethod { get; set; }

    /// <summary>Campos do tipo em JSON (chave Pix, linha digitável, conta, titular): dado pessoal, mascarado na auditoria.</summary>
    [PersonalData]
    public string? PaymentDetails { get; set; }

    public DateTimeOffset? PaymentMethodChangedAt { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public uint Version { get; set; }

    public decimal Retentions => RetentionIss + RetentionInss + RetentionIr + RetentionPcc;

    public decimal NetAmount => Amount - Retentions;

    public decimal Balance => NetAmount - PaidAmount;

    /// <summary>Numa OP não finalizada: valor, fornecedor, retenções, forma e conta ficam travados (§2.2-C1).</summary>
    public bool InPaymentOrder => Status is PayableStatus.AwaitingApproval or PayableStatus.Approved;

    public bool IsOpen => Status is PayableStatus.Open or PayableStatus.PartiallyPaid;

    public bool IsOverdue(DateOnly today) => (IsOpen || InPaymentOrder) && DueDate < today;

    public bool IsDueToday(DateOnly today) => (IsOpen || InPaymentOrder) && DueDate == today;
}

/// <summary>
/// Despesa recorrente (<c>finance.recurring_expenses</c>, FIN-02 CA 16 a 20): o job diário gera um título por competência,
/// <see cref="LeadDays"/> dias antes do vencimento. Com título gerado, não é excluída: encerra pelo fim.
/// </summary>
public sealed class RecurringExpense : Entity, IAuditable, ISoftDeletable
{
    public string Description { get; set; } = string.Empty;

    public Guid PartyId { get; set; }

    public Guid AccountId { get; set; }

    public Guid? CostCenterId { get; set; }

    public decimal Amount { get; set; }

    public PaymentMethodKind? PaymentMethod { get; set; }

    [PersonalData]
    public string? PaymentDetails { get; set; }

    /// <summary>Dia do vencimento (1 a 31); no mês sem esse dia, o último.</summary>
    public int DueDay { get; set; }

    /// <summary>Primeira competência (primeiro dia do mês).</summary>
    public DateOnly Start { get; set; }

    /// <summary>Última competência, se encerrada.</summary>
    public DateOnly? End { get; set; }

    public int LeadDays { get; set; } = 10;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public void MarkDeleted(DateTimeOffset at)
    {
        IsDeleted = true;
        DeletedAt = at;
    }

    /// <summary>Vencimento da competência (primeiro dia do mês): o dia escolhido ou o último do mês.</summary>
    public DateOnly DueDateOf(DateOnly competence) =>
        new(competence.Year, competence.Month, Math.Min(DueDay, DateTime.DaysInMonth(competence.Year, competence.Month)));
}
