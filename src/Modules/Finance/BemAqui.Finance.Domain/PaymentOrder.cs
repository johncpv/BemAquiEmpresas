using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Ordem de pagamento (<c>finance.payment_orders</c>, FIN-03a, R-FIN-5): nenhum dinheiro sai sem OP aprovada. Total = Σ total
/// aprovado dos itens. As transições passam pela entidade rastreada com <c>xmin</c> (ADR 0019 §8).
/// </summary>
[DocumentName("A ordem")]
public sealed class PaymentOrder : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; set; } = string.Empty;

    public PaymentOrderStatus Status { get; set; } = PaymentOrderStatus.Pending;

    public decimal Total { get; set; }

    /// <summary>Data prevista do pagamento (padrão: o menor vencimento).</summary>
    public DateOnly ExpectedDate { get; set; }

    public Guid? RequestedBy { get; init; }

    public DateTimeOffset SubmittedAt { get; set; }

    /// <summary>Aprovada pelo próprio solicitante, nas exceções da D6 (CA 9).</summary>
    public bool SelfApproved { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public DateOnly? PaidOn { get; set; }

    public Guid? PaidBy { get; set; }

    public Guid? BankAccountId { get; set; }

    /// <summary>Paga por quem fechou a folha, na exceção do Proprietário único (RH-02c CA 11): a marca "autopagamento".</summary>
    public bool SelfPaid { get; set; }

    public DateTimeOffset? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public string? ReversalReason { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public uint Version { get; set; }
}

/// <summary>
/// Título na OP (<c>finance.payment_order_items</c>): o valor a pagar, os acréscimos previstos, o total aprovado e o retrato
/// do envio (fornecedor e forma de pagamento), que não muda depois (CA 4).
/// </summary>
public sealed class PaymentOrderItem : Entity, IAuditable
{
    public Guid PaymentOrderId { get; init; }

    public Guid PayableId { get; init; }

    public decimal Amount { get; set; }

    public decimal ExpectedCharges { get; set; }

    public decimal ApprovedTotal { get; set; }

    /// <summary>Retrato do envio em JSON: dado pessoal, mascarado na auditoria.</summary>
    [PersonalData]
    public string PayeeSnapshot { get; set; } = "{}";
}

public enum PaymentOrderDecisionKind
{
    Decision,
    Resubmission,
}

public enum PaymentOrderDecision
{
    Approved,
    Rejected,
    Resubmitted,
}

/// <summary>Decisão ou reenvio da OP (<c>finance.payment_order_approvals</c>): a linha do tempo da ordem (CA 7, 8 e 14).</summary>
public sealed class PaymentOrderApproval : Entity, IAuditable
{
    public Guid PaymentOrderId { get; init; }

    public int Level { get; init; } = 1;

    public PaymentOrderDecisionKind Kind { get; init; }

    public PaymentOrderDecision Decision { get; init; }

    public string? Comment { get; init; }

    public Guid? DecidedBy { get; init; }

    public DateTimeOffset DecidedAt { get; init; }

    public bool SelfApproved { get; init; }
}

/// <summary>
/// Baixa de um título a pagar pela OP (<c>finance.payable_settlements</c>), simétrica à do A receber: imutável, exceto as
/// colunas do estorno. O total que sai da conta é principal + juros + multa − desconto.
/// </summary>
public sealed class PayableSettlement : Entity, IAuditable
{
    public Guid PayableId { get; init; }

    public Guid PaymentOrderId { get; init; }

    public Guid BankAccountId { get; init; }

    public DateOnly Date { get; init; }

    public decimal Principal { get; init; }

    public decimal Interest { get; init; }

    public decimal Fine { get; init; }

    public decimal Discount { get; init; }

    public decimal Total { get; init; }

    public string? BackdateReason { get; init; }

    public Guid? TransactionId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public string? ReversalReason { get; set; }

    public Guid? ReversalTransactionId { get; set; }
}
