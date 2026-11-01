using BemAqui.Finance.Contracts;
using BemAqui.Purchasing.Contracts;

namespace BemAqui.Purchasing.Application;

/// <summary>Local de estoque para o recebimento (sem exigir as permissões do Estoque).</summary>
public sealed record ReceiptLocationOption(Guid Id, string Code, string Name, bool IsDefault);

/// <summary>
/// Linha do "Registrar recebimento" já preenchida (CA 1): a linha do pedido, o saldo a receber, o máximo com a tolerância, o preço do
/// pedido (só leitura), a unidade e o fator do fornecedor (quando houver código do fornecedor) e se o item controla lote.
/// </summary>
public sealed record GoodsReceiptDefaultLine(
    Guid PoLineId, int Sequence, Guid ItemId, string ItemCode, string ItemName, string UnitCode, bool TracksLot, decimal Ordered, decimal Received, decimal Open,
    decimal MaxQuantity, decimal UnitPrice, string? SupplierUnit, decimal? SupplierFactor);

/// <summary>
/// "Registrar recebimento" preenchido (CA 1, propositivo): o pedido, a data (hoje), o local padrão, as linhas com o saldo, o frete e o IPI
/// proporcionais ao saldo inteiro (editáveis só para baixo), a tolerância e, quando o usuário não pode registrar, o motivo.
/// </summary>
public sealed record GoodsReceiptDefaults(
    Guid OrderId, string OrderNumber, PurchaseOrderStatus Status, Guid PartyId, string PartyName, Guid? ProjectId, string? ProjectNumber, DateOnly Today, Guid? DefaultLocationId,
    IReadOnlyList<ReceiptLocationOption> Locations, decimal TolerancePct, decimal OrderSubtotal, decimal OrderFreight, decimal OrderIpiAmount, decimal FreightUsed, decimal IpiUsed,
    decimal ApprovedTotal, decimal ReceivedAmount, IReadOnlyList<GoodsReceiptDefaultLine> Lines, bool SoleOwner, string? Blocker, uint Version);

/// <summary>
/// Linha recebida (CA 1): a linha do pedido, a quantidade (na unidade de estoque, ou na do fornecedor com <c>InSupplierUnit</c>), o lote
/// (obrigatório quando o item controla lote; validade opcional) e o local (padrão: o local padrão do estoque). Quantidade 0 = não veio.
/// </summary>
public sealed record GoodsReceiptLineInput(Guid PoLineId, decimal Quantity, bool InSupplierUnit, string? LotNumber, DateOnly? LotExpiry, Guid? LocationId);

/// <summary>
/// "Registrar recebimento" (CA 1 a 5): data (padrão hoje, ≤ hoje), frete e IPI deste recebimento (null = o proporcional; só para baixo),
/// observação, as linhas e a versão do pedido (409 se mudou).
/// </summary>
public sealed record GoodsReceiptInput(DateOnly? ReceivedOn, decimal? Freight, decimal? IpiAmount, string? Notes, IReadOnlyList<GoodsReceiptLineInput>? Lines, uint Version);

/// <summary>Impacto por item (CA 5): quantidade, rateio, custo unitário de entrada e o custo médio antes → depois (com <c>inventory.view</c>).</summary>
public sealed record GoodsReceiptPreviewLine(
    Guid PoLineId, string ItemCode, string ItemName, string UnitCode, decimal Quantity, decimal LineAmount, decimal FreightShare, decimal IpiShare, decimal UnitCost,
    decimal? AverageCostBefore, decimal? AverageCostAfter, decimal InsuranceShare = 0m, decimal OtherShare = 0m, decimal TaxCreditShare = 0m);

/// <summary>Título a pagar que nasce com o recebimento (CA 5, 8): parcela, vencimento e valor.</summary>
public sealed record GoodsReceiptInstallmentPreview(int Number, DateOnly DueDate, decimal Amount);

/// <summary>
/// Impacto do recebimento para o diálogo de confirmação (CA 5), calculado pelo servidor sem gravar nada: por item, os títulos que nascem,
/// o previsto do pedido antes → depois, o frete e o IPI máximos e o teto.
/// </summary>
public sealed record GoodsReceiptPreview(
    DateOnly ReceivedOn, decimal Subtotal, decimal Freight, decimal IpiAmount, decimal Amount, decimal FreightMax, decimal IpiMax, decimal Ceiling, decimal ReceivedAmountAfter,
    IReadOnlyList<GoodsReceiptPreviewLine> Lines, IReadOnlyList<GoodsReceiptInstallmentPreview> Installments, decimal PlannedBefore, decimal PlannedAfter, bool OrderCompletes,
    bool SoleOwner, decimal Insurance = 0m, decimal OtherExpenses = 0m, decimal TaxCredit = 0m);

public sealed record GoodsReceiptLineView(
    Guid Id, int Sequence, Guid PoLineId, Guid ItemId, string ItemCode, string ItemName, string UnitCode, decimal Quantity, string? SupplierUnit, decimal? SupplierQuantity,
    string? LotNumber, DateOnly? LotExpiry, Guid LocationId, string LocationName, decimal UnitPrice, decimal LineAmount, decimal FreightShare, decimal IpiShare, decimal UnitCost,
    Guid? MovementId, string? MovementNumber, Guid? ReversalMovementId, decimal InsuranceShare = 0m, decimal OtherShare = 0m, decimal TaxCreditShare = 0m);

/// <summary>OS que recebeu material desta entrada (CA 7, "Ver OS que receberam material").</summary>
public sealed record ServedOrderRef(Guid Id, string Number);

/// <summary>Detalhe do recebimento (CA 5, 12, 15 e 18): cabeçalho, valores, linhas com o movimento, as OS atendidas e o estorno.</summary>
public sealed record GoodsReceiptDetail(
    Guid Id, string Number, GoodsReceiptStatus Status, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid PartyId, string PartyName, Guid? ProjectId, string? ProjectNumber,
    DateOnly ReceivedOn, decimal Subtotal, decimal Freight, decimal IpiAmount, decimal Amount, string? Notes, Guid? ReceivedBy, string? ReceivedByName, bool SoleOwner,
    DateTimeOffset CreatedAt, DateTimeOffset? ReversedAt, string? ReversedByName, string? ReversalReason, IReadOnlyList<GoodsReceiptLineView> Lines,
    IReadOnlyList<ServedOrderRef> ServedOrders, bool CanReverse, uint Version, GoodsReceiptOrigin Origin = GoodsReceiptOrigin.Manual, Guid? IncomingInvoiceId = null,
    decimal Insurance = 0m, decimal OtherExpenses = 0m, decimal TaxCredit = 0m);

/// <summary>Linha da aba "Recebidos" e da aba Recebimentos do pedido (CA 15).</summary>
public sealed record GoodsReceiptSummary(
    Guid Id, string Number, GoodsReceiptStatus Status, DateOnly ReceivedOn, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid PartyId, string PartyName, string ItemSummary,
    decimal Amount);

/// <summary>Filtros da aba "Recebidos": situação, fornecedor, pedido e o número.</summary>
public sealed record GoodsReceiptListQuery(GoodsReceiptStatus? Status, Guid? PartyId, Guid? PurchaseOrderId, string? Term);

/// <summary>"Estornar recebimento" (CA 12): motivo obrigatório e a versão.</summary>
public sealed record GoodsReceiptReverseInput(string? Reason, uint Version);

/// <summary>Título do recebimento na aba Financeiro (com <c>finance.view</c>).</summary>
public sealed record GoodsReceiptPayableView(Guid Id, string Number, short Installment, PayableStatus Status, decimal Amount, DateOnly? DueDate, Guid? PaymentOrderId);

/// <summary>"Última compra" do item (CA 15): o preço, o fornecedor e o pedido.</summary>
public sealed record LastPurchaseView(Guid ItemId, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid PartyId, string PartyName, decimal UnitPrice, DateTimeOffset ApprovedAt);
