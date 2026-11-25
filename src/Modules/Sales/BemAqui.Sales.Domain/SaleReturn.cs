using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Sales.Domain;

/// <summary>
/// Devolução de venda (<c>sales.sale_returns</c>, PDV-06): documento novo ligado à venda original, que fica como está (nunca apagada). Nasce pedida
/// no caixa (com rede) e conclui na aprovação, com o estoque, o reembolso e a venda original na mesma transação. Troca = devolução com vale + venda nova
/// paga com o vale (<see cref="ExchangeSaleId"/>). Auditada: pedido, aprovação e conclusão ficam no histórico.
/// </summary>
[DocumentName("A devolução")]
public sealed class SaleReturn : Entity, IAuditable, IConcurrencyVersioned
{
    public const string DocType = "DEV";
    public const int MaxNoteLength = 200;
    public const int MaxNsuLength = 12;

    public string Number { get; init; } = string.Empty;

    public Guid SaleId { get; init; }

    public SaleReturnStatus Status { get; private set; } = SaleReturnStatus.Requested;

    public RefundKind RefundKind { get; init; }

    /// <summary>Total devolvido (<c>numeric(18,2)</c>): a soma das linhas, pelo valor praticado na venda (com o desconto rateado).</summary>
    public decimal Total { get; init; }

    /// <summary>Loja, caixa e aparelho onde a devolução aconteceu (do cookie do aparelho, nunca do corpo).</summary>
    public Guid StoreId { get; init; }

    public Guid RegisterId { get; init; }

    public Guid DeviceId { get; init; }

    /// <summary>Sessão de caixa aberta no pedido; o dinheiro da devolução em dinheiro sai dela.</summary>
    public Guid? CashSessionId { get; init; }

    public Guid OperatorId { get; init; }

    /// <summary>Fora do prazo de troca da loja (CA 3): pede supervisor. <see cref="Deadline"/> é o último dia do prazo.</summary>
    public bool Late { get; private set; }

    public DateOnly Deadline { get; init; }

    /// <summary>Estorno no cartão (CA 5): o pagamento do cartão da venda e o NSU do estorno feito na maquininha.</summary>
    public Guid? CardPaymentId { get; init; }

    public string? CardNsu { get; init; }

    /// <summary>Troca (CA 9): a devolução vira vale para a venda nova; a venda nova fica ligada quando usa o vale.</summary>
    public bool IsExchange { get; init; }

    public Guid? ExchangeSaleId { get; private set; }

    public Guid? CustomerId { get; init; }

    public string? Note { get; init; }

    public Guid? StoreCreditId { get; private set; }

    public Guid? SupervisorApprovalId { get; private set; }

    public Guid? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Data de negócio (America/Sao_Paulo) da conclusão: o relatório e o Financeiro agrupam por ela.</summary>
    public DateOnly? BusinessDate { get; private set; }

    public DateTimeOffset RequestedAt { get; init; }

    public uint Version { get; set; }

    public bool NeedsSupervisor => Late || RefundKind == RefundKind.Cash;

    public static SaleReturn Request(Guid id, string number, Guid saleId, RefundKind refundKind, decimal total, Guid storeId, Guid registerId, Guid deviceId, Guid? cashSessionId,
        Guid operatorId, bool late, DateOnly deadline, Guid? cardPaymentId, string? cardNsu, bool isExchange, Guid? customerId, string? note, DateTimeOffset at) =>
        new()
        {
            Id = id, Number = number, SaleId = saleId, RefundKind = refundKind, Total = total, StoreId = storeId, RegisterId = registerId, DeviceId = deviceId,
            CashSessionId = cashSessionId, OperatorId = operatorId, Late = late, Deadline = deadline, CardPaymentId = cardPaymentId, CardNsu = cardNsu, IsExchange = isExchange,
            CustomerId = customerId, Note = note, RequestedAt = at,
        };

    /// <summary>Aprovada (CA 3 e 5: com o supervisor quando pede; sem, pelo próprio operador com <c>pos.return</c>) e concluída na mesma transação.</summary>
    public void Complete(Guid approvedBy, Guid? approvalId, Guid? storeCreditId, DateTimeOffset at, DateOnly businessDate)
    {
        if (Status != SaleReturnStatus.Requested)
        {
            throw new InvalidOperationException("A devolução " + Number + " não está pedida.");
        }

        Status = SaleReturnStatus.Approved;
        ApprovedBy = approvedBy;
        ApprovedAt = at;
        SupervisorApprovalId = approvalId;
        StoreCreditId = storeCreditId;
        Status = SaleReturnStatus.Completed;
        CompletedAt = at;
        BusinessDate = businessDate;
    }

    /// <summary>Revisão de segurança (achado 10): o prazo venceu entre o pedido e a conclusão; a conclusão passa a pedir o supervisor.</summary>
    public void MarkLate() => Late = true;

    /// <summary>Troca (CA 9): a primeira venda que usa o vale desta devolução fica ligada a ela.</summary>
    public void LinkExchange(Guid saleId) => ExchangeSaleId ??= saleId;
}

/// <summary>Destino do item devolvido (CA 4): volta ao estoque ou avariado (perda).</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<ReturnDestination>))]
public enum ReturnDestination
{
    Restock,
    Damaged,
}

/// <summary>
/// Linha da devolução (<c>sales.sale_return_lines</c>): a linha da venda, a quantidade (<c>numeric(18,4)</c>), o valor devolvido (<c>numeric(18,2)</c>),
/// o destino com o motivo, e o movimento de estoque (entrada de devolução; na perda, a entrada e a baixa de perda).
/// </summary>
public sealed class SaleReturnLine : Entity
{
    public const int MaxReasonLength = 140;

    public Guid ReturnId { get; init; }

    public Guid SaleLineId { get; init; }

    public Guid ProductId { get; init; }

    public Guid? ItemId { get; init; }

    public string Description { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public decimal Amount { get; init; }

    public ReturnDestination Destination { get; init; }

    public string Reason { get; init; } = string.Empty;

    public Guid? StockMovementId { get; set; }

    public Guid? LossMovementId { get; set; }

    /// <summary>Por que o estoque não mexeu (serviço, item com lote ou série): o estoquista lança pelo Estoque.</summary>
    public string? StockNote { get; set; }

    public static SaleReturnLine Create(Guid returnId, SaleLine line, decimal quantity, decimal amount, ReturnDestination destination, string reason) =>
        new()
        {
            Id = Guid.CreateVersion7(), ReturnId = returnId, SaleLineId = line.Id, ProductId = line.ProductId, ItemId = line.ItemId, Description = line.Description,
            Quantity = quantity, Amount = amount, Destination = destination, Reason = reason,
        };
}

/// <summary>
/// Vale-troca (<c>sales.store_credits</c>, PDV-06 CA 7): passivo da loja com saldo (<c>numeric(18,2)</c>), validade e cliente opcional. Valor ao
/// portador: o código só existe no papel; aqui ficam o hash (SHA-256) e os 4 últimos caracteres. Só funciona com rede. Auditado (emissão e cada uso).
/// </summary>
[DocumentName("O vale-troca")]
public sealed class StoreCredit : Entity, IAuditable, IConcurrencyVersioned
{
    /// <summary>Fora da auditoria: o hash do valor ao portador não vai para o histórico.</summary>
    [NotAudited]
    public string CodeHash { get; private set; } = string.Empty;

    public string CodeLast4 { get; private set; } = string.Empty;

    public Guid? CustomerId { get; init; }

    public decimal Amount { get; init; }

    public decimal Balance { get; private set; }

    public DateOnly ExpiresOn { get; init; }

    public Guid OriginReturnId { get; init; }

    public Guid StoreId { get; init; }

    public DateTimeOffset IssuedAt { get; init; }

    public uint Version { get; set; }

    public static StoreCredit Issue(string codeHash, string last4, Guid? customerId, decimal amount, DateOnly expiresOn, Guid originReturnId, Guid storeId, DateTimeOffset at) =>
        new()
        {
            Id = Guid.CreateVersion7(), CodeHash = codeHash, CodeLast4 = last4, CustomerId = customerId, Amount = amount, Balance = amount, ExpiresOn = expiresOn,
            OriginReturnId = originReturnId, StoreId = storeId, IssuedAt = at,
        };

    /// <summary>CA 8: abate o valor; o restante continua no mesmo vale. Quem chama já conferiu validade e saldo.</summary>
    public void Debit(decimal amount) => Balance = SaleRules.Money(Balance - amount);

    /// <summary>Venda com o vale cancelada ou descartada: o valor volta ao vale.</summary>
    public void Credit(decimal amount) => Balance = SaleRules.Money(Balance + amount);

    /// <summary>Revisão de segurança (achado 5): reemissão do código perdido, com o supervisor. O hash e os 4 últimos mudam: o código antigo deixa de valer.</summary>
    public void Reissue(string codeHash, string last4)
    {
        CodeHash = codeHash;
        CodeLast4 = last4;
    }
}

/// <summary>Movimento do vale-troca (<c>sales.store_credit_movements</c>): o uso numa venda (por pagamento, idempotente) e o desfazer do uso.</summary>
public sealed class StoreCreditMovement : Entity
{
    public Guid StoreCreditId { get; init; }

    /// <summary>O pagamento do caixa que usou o vale (id do aparelho): reenviar não abate de novo.</summary>
    public Guid TenderId { get; init; }

    public Guid SaleId { get; init; }

    public decimal Amount { get; init; }

    public Guid OperatorId { get; init; }

    public Guid RegisterId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReversedAt { get; set; }

    public string? ReversalReason { get; set; }
}
