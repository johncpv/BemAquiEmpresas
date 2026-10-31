using System.Text.Json.Serialization;
using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Título a receber (<c>finance.receivables</c>, FIN-01, R-FIN-3). Nunca é apagado, só cancelado. O invariante
/// <c>PaidAmount = Σ principal das baixas não estornadas</c> é mantido por <see cref="Recalculate"/>; "Vencido" é calculado
/// (<see cref="IsOverdue"/>), nunca gravado.
/// </summary>
[DocumentName("O título")]
public sealed class Receivable : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; set; } = string.Empty;

    public Guid PartyId { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Conta do plano de contas (Receita, analítica).</summary>
    public Guid AccountId { get; set; }

    public Guid? CostCenterId { get; set; }

    public Guid? ProjectId { get; set; }

    public decimal Amount { get; set; }

    public decimal PaidAmount { get; private set; }

    public decimal Interest { get; private set; }

    public decimal Fine { get; private set; }

    public decimal Discount { get; private set; }

    public DateOnly IssueDate { get; set; }

    /// <summary>Nulo só em <see cref="ReceivableStatus.Planned"/> (marco por evento, COM-04b).</summary>
    public DateOnly? DueDate { get; set; }

    public ReceivableStatus Status { get; set; } = ReceivableStatus.Open;

    public DocumentOrigin Origin { get; set; } = DocumentOrigin.Manual;

    /// <summary>Documento de origem do título de implantação (FIN-00), ex.: a fatura no Dolibarr.</summary>
    public string? OpeningReference { get; set; }

    public Guid? OriginId { get; set; }

    /// <summary>Contrato da parcela (COM-04b): o <see cref="OriginId"/> é o marco.</summary>
    public Guid? ContractId { get; set; }

    /// <summary>Evento que libera a parcela <see cref="ReceivableStatus.Planned"/> (COM-04b CA 1).</summary>
    public TriggerEvent? TriggerEvent { get; set; }

    /// <summary>Dias corridos depois do evento para o vencimento liberado.</summary>
    public int? DaysAfterEvent { get; set; }

    /// <summary>Parcelas lançadas juntas pela condição de pagamento.</summary>
    public Guid? InstallmentGroupId { get; set; }

    public string? CancelReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    /// <summary>Boleto em andamento do título (BNK-01b): pedido, emitido ou com falha à espera de nova tentativa; nulo sem boleto.</summary>
    public Guid? BillingSlipId { get; set; }

    public uint Version { get; set; }

    public decimal Balance => Amount - PaidAmount;

    public bool IsOpen => Status is ReceivableStatus.Open or ReceivableStatus.PartiallyPaid;

    public bool IsOverdue(DateOnly today) => IsOpen && DueDate < today;

    public bool IsDueToday(DateOnly today) => IsOpen && DueDate == today;

    /// <summary>Refaz saldo, juros, multa, desconto e situação a partir das baixas ativas (invariante da FIN-01 CA 12).</summary>
    public void Recalculate(IEnumerable<ReceivableSettlement> settlements)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        var active = settlements.Where(s => s.ReceivableId == Id && s.ReversedAt is null).ToList();
        PaidAmount = active.Sum(s => s.Principal);
        Interest = active.Sum(s => s.Interest);
        Fine = active.Sum(s => s.Fine);
        Discount = active.Sum(s => s.Discount);
        if (Status is ReceivableStatus.Cancelled or ReceivableStatus.Planned)
        {
            return;
        }

        Status = PaidAmount <= 0 ? ReceivableStatus.Open : PaidAmount >= Amount ? ReceivableStatus.Paid : ReceivableStatus.PartiallyPaid;
    }
}

/// <summary>Tipo de baixa: recebimento comum ou quitação do saldo com desconto, sem transação (ADR 0019 §8).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SettlementKind>))]
public enum SettlementKind
{
    Settlement,
    DiscountWriteOff,
}

/// <summary>Origem da baixa (BNK-02): a do banco só se estorna com <c>finance.approve</c> e o 2FA de novo (CA 14, D6).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SettlementSource>))]
public enum SettlementSource
{
    Manual,
    Bank,
}

/// <summary>
/// Baixa de um título a receber (<c>finance.receivable_settlements</c>): imutável, exceto as colunas do estorno. O total que
/// entra na conta é principal + juros + multa − desconto.
/// </summary>
public sealed class ReceivableSettlement : Entity, IAuditable
{
    public Guid ReceivableId { get; init; }

    /// <summary>Nulo na quitação com desconto.</summary>
    public Guid? BankAccountId { get; init; }

    public DateOnly Date { get; init; }

    public decimal Principal { get; init; }

    public decimal Interest { get; init; }

    public decimal Fine { get; init; }

    public decimal Discount { get; init; }

    public decimal Total { get; init; }

    /// <summary>Saldo do título antes desta baixa: base do % de desconto (relatório e limite, CA 13 e 22).</summary>
    public decimal BalanceBefore { get; init; }

    public SettlementKind Kind { get; init; }

    public string? BackdateReason { get; init; }

    /// <summary>De onde veio a baixa: manual (FIN-01) ou do banco (aviso do Inter, conferência diária ou conferência do pagamento, BNK-02).</summary>
    public SettlementSource Source { get; init; } = SettlementSource.Manual;

    /// <summary>Boleto pago que gerou a baixa do banco.</summary>
    public Guid? BillingSlipId { get; init; }

    /// <summary>Evento do webhook de origem (histórico do título, BNK-02 CA 17); nulo na conferência diária.</summary>
    public Guid? WebhookEventId { get; init; }

    public Guid? TransactionId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public string? ReversalReason { get; set; }

    public Guid? ReversalTransactionId { get; set; }

    public static decimal TotalOf(decimal principal, decimal interest, decimal fine, decimal discount) =>
        Math.Round(principal + interest + fine - discount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Movimento numa conta bancária (<c>finance.financial_transactions</c>): valor com sinal (+ entrada), imutável; o estorno é
/// outra transação, ligada à original. Saldo da conta = saldo inicial + Σ valores.
/// </summary>
public sealed class FinancialTransaction : Entity, IAuditable
{
    public Guid BankAccountId { get; init; }

    public DateOnly Date { get; init; }

    public decimal Amount { get; init; }

    public TransactionType Type { get; init; }

    public string Description { get; init; } = string.Empty;

    public Guid? ReceivableId { get; init; }

    /// <summary>Baixa da transação; na entrada que aguardava conferência, ligada só quando alguém confere (BNK-02 CA 9).</summary>
    public Guid? SettlementId { get; set; }

    /// <summary>
    /// Entrada do Inter aguardando conferência (BNK-02 CA 9): o dinheiro está na conta (o saldo bate com o banco), mas o título ainda
    /// não foi baixado. A conferência liga a baixa e tira a marca; o valor nunca muda.
    /// </summary>
    public bool PendingReview { get; private set; }

    /// <summary>Boleto pago de onde veio a entrada (BNK-02).</summary>
    public Guid? BillingSlipId { get; init; }

    /// <summary>Título a pagar do pagamento da OP (FIN-03a).</summary>
    public Guid? PayableId { get; init; }

    /// <summary>Transferência entre contas (FIN-04): as duas pontas levam o mesmo id.</summary>
    public Guid? TransferId { get; init; }

    /// <summary>Lançamento sem OP (FIN-04).</summary>
    public Guid? BankEntryId { get; init; }

    public Guid? ReversesTransactionId { get; init; }

    /// <summary>Entrada recebida do banco aguardando conferência (CA 9).</summary>
    public FinancialTransaction AwaitingReview()
    {
        PendingReview = true;
        return this;
    }

    /// <summary>Conferida: a marca sai; com a baixa, a transação passa a ser dela.</summary>
    public void Reviewed(Guid? settlementId)
    {
        PendingReview = false;
        if (settlementId is { } id)
        {
            SettlementId = id;
        }
    }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }
}

/// <summary>
/// Parâmetro do Finance (<c>finance.parameters</c>, ADR 0016 adendo M6): chave e valor numérico (ou <see cref="Text"/>, como o
/// id de uma conta do plano), com auditoria. Os valores que dependem da contadora (ADR 0042) levam a marca de conferência:
/// <see cref="ReviewState"/>, quem e quando; trocar o valor volta para "A confirmar".
/// </summary>
public sealed class FinanceParameter : Entity, IAuditable
{
    public string Key { get; init; } = string.Empty;

    public decimal Value { get; set; }

    /// <summary>Valor em texto (FIN-08: id da conta do plano dos parâmetros do DRE).</summary>
    public string? Text { get; set; }

    /// <summary>Nulo nos parâmetros que não dependem da contadora.</summary>
    public ParameterReviewState? ReviewState { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Valor novo: a conferência valia para o valor de antes (ADR 0042 §1).</summary>
    public void ResetReview()
    {
        ReviewState = ParameterReviewState.ToConfirm;
        ReviewedBy = null;
        ReviewedAt = null;
    }

    public void Confirm(Guid? by, DateTimeOffset at)
    {
        ReviewState = ParameterReviewState.Confirmed;
        ReviewedBy = by;
        ReviewedAt = at;
    }
}

/// <summary>Conferência de um valor pré-configurado (ADR 0042): "A confirmar com a contadora" até alguém marcar como conferido.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ParameterReviewState>))]
public enum ParameterReviewState
{
    ToConfirm,
    Confirmed,
}
