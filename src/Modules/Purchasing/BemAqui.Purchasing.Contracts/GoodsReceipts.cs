using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Purchasing.Contracts;

/// <summary>Situação do recebimento (<c>purchasing.goods_receipts.status</c>, CMP-04): registrado ou estornado. Nunca é apagado.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GoodsReceiptStatus>))]
public enum GoodsReceiptStatus
{
    Completed,
    Reversed,
}

/// <summary>Última compra do item (CMP-04 CA 15): o preço e o fornecedor do último pedido aprovado (ou além), para a CMP-02 e a CMP-03a.</summary>
public sealed record LastPurchaseInfo(Guid ItemId, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid PartyId, decimal UnitPrice, DateTimeOffset ApprovedAt);

/// <summary>Linha do <see cref="GoodsReceived"/>: a linha do pedido, a quantidade (unidade de estoque) e o valor da linha (qtd × preço).</summary>
public sealed record GoodsReceivedLine(Guid PoLineId, decimal Quantity, decimal Amount);

/// <summary>
/// Recebimento registrado (CMP-04 CA 5): <b>só aviso e projeção</b> (notificação, fluxo de caixa ao vivo, comprometido do projeto). A
/// entrada no estoque e os títulos já foram gravados na mesma transação (ADR 0023 §1, ADR 0024 §1); nenhum consumidor do Finance cria,
/// confirma ou cancela título por este evento.
/// </summary>
public sealed record GoodsReceived(
    Guid GoodsReceiptId, Guid PurchaseOrderId, Guid PartyId, Guid? ProjectId, DateOnly ReceivedOn, decimal Amount, IReadOnlyList<GoodsReceivedLine> Lines) : IIntegrationEvent;

/// <summary>Recebimento estornado (CA 12): só aviso; os títulos, o estoque e o previsto do pedido já mudaram na mesma transação.</summary>
public sealed record GoodsReceiptReversed(Guid GoodsReceiptId, Guid PurchaseOrderId, decimal Amount) : IIntegrationEvent;

/// <summary>Saldo do pedido encerrado (CA 13): só aviso; os previstos restantes já foram cancelados na mesma transação.</summary>
public sealed record PurchaseOrderClosed(Guid PurchaseOrderId, decimal ClosedBalanceAmount) : IIntegrationEvent;

/// <summary>Preço da linha do pedido corrigido (CA 14): só aviso; vale para o saldo a receber.</summary>
public sealed record PurchaseOrderPriceCorrected(Guid PurchaseOrderId, Guid PoLineId, decimal PreviousPrice, decimal NewPrice, bool Reapproval) : IIntegrationEvent;
