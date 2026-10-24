using BemAqui.Purchasing.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Domain;

/// <summary>
/// Recebimento manual contra o pedido (<c>purchasing.goods_receipts</c>, CMP-04): número <c>REC-AAAA-0001</c>, data de recebimento, os
/// valores conferidos (itens + frete + IPI rateados, P-16) e quem recebeu, com a marca do Proprietário único. A entrada no estoque e os
/// títulos do recebimento nascem na mesma transação (ADR 0023 §1, ADR 0024 §3). Nunca é apagado: só estornado.
/// </summary>
[DocumentName("O recebimento")]
public sealed class GoodsReceipt : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; init; } = string.Empty;

    public Guid PurchaseOrderId { get; init; }

    public Guid PartyId { get; init; }

    /// <summary>Projeto do pedido, só como referência (a entrada não vira custo do projeto, ADR 0025 §1).</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Nota de entrada (FIS-05/FIS-06 reaproveitam este fluxo); null no recebimento manual.</summary>
    public Guid? IncomingInvoiceId { get; init; }

    /// <summary>Data de recebimento (data de negócio, ≤ hoje): base do vencimento das parcelas.</summary>
    public DateOnly ReceivedOn { get; init; }

    public GoodsReceiptStatus Status { get; set; } = GoodsReceiptStatus.Completed;

    /// <summary>Σ (qtd × preço do pedido) das linhas, 2 casas.</summary>
    public decimal Subtotal { get; init; }

    public decimal Freight { get; init; }

    public decimal IpiAmount { get; init; }

    /// <summary>Valor do recebimento (títulos): itens + frete + IPI deste recebimento, 2 casas.</summary>
    public decimal Amount { get; init; }

    /// <summary>Situação do pedido antes deste recebimento (o estorno do único recebimento volta a ela).</summary>
    public PurchaseOrderStatus OrderStatusBefore { get; init; }

    public string? Notes { get; init; }

    public Guid? ReceivedBy { get; init; }

    /// <summary>"Recebido pelo Proprietário único" (CA 2): quem pediu ou aprovou recebeu porque não havia outro com <c>purchasing.receive</c>.</summary>
    public bool SoleOwner { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public string? ReversalReason { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// Linha do recebimento (<c>purchasing.goods_receipt_lines</c>): a linha do pedido, a quantidade na unidade de estoque (e a do fornecedor,
/// quando informada), lote, local, o preço do pedido, o rateio do frete e do IPI, o custo unitário de entrada (6 casas) e o movimento.
/// </summary>
[DocumentName("A linha do recebimento")]
public sealed class GoodsReceiptLine : Entity, IAuditable
{
    public Guid ReceiptId { get; init; }

    public int Sequence { get; init; }

    public Guid PoLineId { get; init; }

    public Guid ItemId { get; init; }

    public decimal Quantity { get; init; }

    public string? SupplierUnit { get; init; }

    public decimal? SupplierQuantity { get; init; }

    public string? LotNumber { get; init; }

    public Guid? LotId { get; init; }

    public DateOnly? LotExpiry { get; init; }

    public Guid LocationId { get; init; }

    /// <summary>Preço unitário do pedido no momento do recebimento (só leitura; muda só pela correção de preço, CA 14).</summary>
    public decimal UnitPrice { get; init; }

    /// <summary>Valor da linha: qtd × preço, 2 casas.</summary>
    public decimal LineAmount { get; init; }

    public decimal FreightShare { get; init; }

    public decimal IpiShare { get; init; }

    /// <summary>Custo unitário de entrada (P-16): (valor + rateio do frete + rateio do IPI) ÷ quantidade, 6 casas.</summary>
    public decimal UnitCost { get; init; }

    public Guid? MovementId { get; set; }

    public string? MovementNumber { get; set; }

    public Guid? ReversalMovementId { get; set; }
}

/// <summary>
/// Correção de preço de uma linha do pedido (<c>purchasing.purchase_order_price_corrections</c>, CA 14): o preço anterior, o novo, o motivo
/// e quem corrigiu. Vale só para o saldo a receber; acima do teto o pedido volta à alçada. Imutável.
/// </summary>
public sealed class PurchaseOrderPriceCorrection : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    public Guid PoLineId { get; init; }

    public decimal PreviousPrice { get; init; }

    public decimal NewPrice { get; init; }

    public string Reason { get; init; } = string.Empty;

    /// <summary>Acima do teto (aprovado + tolerância): o pedido voltou à alçada.</summary>
    public bool Reapproval { get; init; }

    public Guid? CorrectedBy { get; init; }

    public DateTimeOffset CorrectedAt { get; init; }
}
