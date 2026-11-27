using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Sales.Domain;

/// <summary>Como o pedido foi pago: pela baixa do título do boleto (BNK-02) ou registrado à mão (dinheiro, maquininha, Pix recebido fora).</summary>
public enum SalesOrderPaymentKind
{
    BankSlip,
    Manual,
}

/// <summary>
/// Pedido de venda (<c>sales.orders</c>, VND-02a): documento comercial, nunca apagado, só cancelado. Enquanto não é pago é só pedido (estoque reservado e,
/// cobrado, o título <c>SalesOrder</c>); pago, vira a venda <see cref="SaleId"/> da VND-01 (o id é reservado na confirmação). A origem, o aplicativo e
/// quem criou vêm do principal autenticado (CA 2). Toda transição passa pelo <c>xmin</c> (<see cref="Version"/>).
/// </summary>
[DocumentName("O pedido")]
public sealed class SalesOrder : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; init; } = string.Empty;

    public SalesOrderStatus Status { get; private set; } = SalesOrderStatus.Draft;

    public SalesOrderOrigin Origin { get; init; }

    /// <summary>Conexão do aplicativo que criou (ADR 0053); nula para pessoa.</summary>
    public Guid? SourceAppId { get; init; }

    /// <summary>Nome do aplicativo, para o rótulo "Pedido de {aplicativo}" e a trilha.</summary>
    public string? SourceAppName { get; init; }

    /// <summary>Id do pedido no sistema de origem; único por origem e aplicativo (CA 2).</summary>
    public string? ExternalId { get; init; }

    /// <summary>Hash do corpo do pedido externo: o mesmo id com outro corpo é 409 <c>external_id_conflict</c>.</summary>
    public string? ExternalHash { get; init; }

    /// <summary>Quem criou: a pessoa da sessão ou quem autorizou o aplicativo.</summary>
    public Guid? CreatedBy { get; init; }

    public Guid CustomerId { get; set; }

    public Guid? StoreId { get; set; }

    public Guid StockLocationId { get; set; }

    /// <summary>Id da venda, reservado na confirmação (a conversão usa o mesmo: idempotente, VND-01 CA 4).</summary>
    public Guid? SaleId { get; private set; }

    /// <summary>Título <c>SalesOrder</c> da cobrança atual.</summary>
    public Guid? ReceivableId { get; set; }

    public SalesOrderPaymentKind? PaymentKind { get; private set; }

    public decimal Subtotal { get; private set; }

    /// <summary>Descontos das linhas + desconto do pedido.</summary>
    public decimal DiscountTotal { get; private set; }

    /// <summary>Desconto do pedido inteiro (além das linhas).</summary>
    public decimal OrderDiscount { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>Até quando a reserva vale (CA 12); nulo no rascunho e no pedido pago.</summary>
    public DateTimeOffset? HoldUntil { get; set; }

    /// <summary>Desconto acima do limite do aplicativo: só rascunho, confirma no ERP quem tem <c>sales.approve</c> (CA 5).</summary>
    public bool DiscountNeedsApproval { get; set; }

    /// <summary>Pago depois de cancelado (CA 19): alerta com "Reabrir pedido" ou "Devolver o valor".</summary>
    public bool PaidAfterCancel { get; set; }

    public string? Note { get; set; }

    public string? CancelReason { get; private set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset? ChargedAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public uint Version { get; set; }

    public bool FromApp => SourceAppId is not null;

    public static SalesOrder Create(
        Guid id, string number, SalesOrderOrigin origin, Guid? sourceAppId, string? sourceAppName, string? externalId, string? externalHash, Guid? createdBy, Guid customerId, Guid? storeId,
        Guid stockLocationId, string? note, DateTimeOffset now) =>
        new()
        {
            Id = id, Number = number, Origin = origin, SourceAppId = sourceAppId, SourceAppName = sourceAppName, ExternalId = externalId, ExternalHash = externalHash, CreatedBy = createdBy,
            CustomerId = customerId, StoreId = storeId, StockLocationId = stockLocationId, Note = note, CreatedAt = now, UpdatedAt = now,
        };

    /// <summary>Totais a partir das linhas e do desconto do pedido (dinheiro em 2 casas).</summary>
    public void ApplyTotals(IReadOnlyCollection<SalesOrderLine> lines, decimal orderDiscount)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Subtotal = SaleRules.Money(lines.Sum(l => l.Gross));
        OrderDiscount = SaleRules.Money(orderDiscount);
        DiscountTotal = SaleRules.Money(lines.Sum(l => l.Discount) + OrderDiscount);
        Total = SaleRules.Money(Subtotal - DiscountTotal);
    }

    public void Confirm(Guid saleId, DateTimeOffset holdUntil, DateTimeOffset now)
    {
        Move(SalesOrderStatus.Open, now);
        SaleId ??= saleId;
        ConfirmedAt = now;
        HoldUntil = holdUntil;
    }

    public void Charged(Guid receivableId, DateTimeOffset holdUntil, DateTimeOffset now)
    {
        Move(SalesOrderStatus.AwaitingPayment, now);
        ReceivableId = receivableId;
        ChargedAt = now;
        HoldUntil = holdUntil;
    }

    /// <summary>Cobrança cancelada ou vencida sem pagamento: volta a <c>Open</c>, com a reserva pelo prazo de pessoa ou de aplicativo.</summary>
    public void ChargeLapsed(DateTimeOffset holdUntil, DateTimeOffset now)
    {
        Move(SalesOrderStatus.Open, now);
        HoldUntil = holdUntil;
    }

    public void Pay(SalesOrderPaymentKind kind, DateTimeOffset now)
    {
        Move(SalesOrderStatus.Paid, now);
        PaymentKind = kind;
        PaidAt = now;
        HoldUntil = null;
        PaidAfterCancel = false;
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        Move(SalesOrderStatus.Cancelled, now);
        CancelReason = reason;
        CancelledAt = now;
        HoldUntil = null;
    }

    private void Move(SalesOrderStatus to, DateTimeOffset now)
    {
        Status = to;
        UpdatedAt = now;
    }
}

/// <summary>
/// Linha do pedido (<c>sales.order_lines</c>): produto vendável, item de estoque, quantidade (<c>numeric(18,4)</c>), preço da tabela padrão lido na criação
/// e relido na confirmação (<see cref="PriceListVersion"/>), desconto e total (<c>numeric(18,2)</c>).
/// </summary>
public sealed class SalesOrderLine : Entity, IAuditable
{
    public Guid OrderId { get; init; }

    public int LineNumber { get; set; }

    public Guid ProductId { get; init; }

    public Guid ItemId { get; init; }

    public string Description { get; init; } = string.Empty;

    public string UnitCode { get; init; } = string.Empty;

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal Discount { get; set; }

    public decimal Total { get; set; }

    public long PriceListVersion { get; set; }

    public decimal Gross => SaleRules.Money(Quantity * UnitPrice);

    public static SalesOrderLine Create(Guid orderId, int lineNumber, Guid productId, Guid itemId, string description, string unitCode, decimal quantity, decimal unitPrice, decimal discount,
        long priceListVersion)
    {
        var line = new SalesOrderLine
        {
            OrderId = orderId, LineNumber = lineNumber, ProductId = productId, ItemId = itemId, Description = description, UnitCode = unitCode,
            Quantity = SaleRules.Quantity(quantity), UnitPrice = SaleRules.Money(unitPrice), Discount = SaleRules.Money(discount), PriceListVersion = priceListVersion,
        };
        line.Total = SaleRules.Money(line.Gross - line.Discount);
        return line;
    }

    public void Reprice(decimal unitPrice, long version)
    {
        UnitPrice = SaleRules.Money(unitPrice);
        PriceListVersion = version;
        Total = SaleRules.Money(Gross - Discount);
    }
}

/// <summary>
/// Linha do tempo do pedido (<c>sales.order_transitions</c>, CA 6 e 29): de que estado para qual, a ação, quem (pessoa ou "Integração {app} (autorizada
/// por {pessoa})"), a origem, o motivo e quando. Só inclusão.
/// </summary>
public sealed class SalesOrderTransition : Entity
{
    public Guid OrderId { get; init; }

    public SalesOrderStatus? FromStatus { get; init; }

    public SalesOrderStatus ToStatus { get; init; }

    /// <summary>Código da ação (<c>create</c>, <c>confirm</c>, <c>charge</c>, <c>pay</c>, <c>cancel</c>…).</summary>
    public string Action { get; init; } = string.Empty;

    public Guid? ActorId { get; init; }

    /// <summary>Rótulo do autor quando não é pessoa ("Banco Inter", "Prazo de pagamento", "Integração Conecta").</summary>
    public string? ActorLabel { get; init; }

    public SalesOrderOrigin Origin { get; init; }

    public string? Reason { get; init; }

    public DateTimeOffset At { get; init; }
}

/// <summary>
/// Parâmetros do pedido de venda (<c>sales.order_parameters</c>, uma linha; Configurações → Parâmetros → Pedidos de venda). Sem a linha, valem os padrões
/// de <see cref="SalesOrderRules"/>. Auditado.
/// </summary>
public sealed class SalesOrderParameters : Entity, IAuditable
{
    public decimal DiscountLimitPct { get; set; } = SalesOrderRules.DefaultDiscountLimitPct;

    public decimal AppDiscountLimitPct { get; set; } = SalesOrderRules.DefaultAppDiscountLimitPct;

    public int HoldHours { get; set; } = SalesOrderRules.DefaultHoldHours;

    public int AppHoldHours { get; set; } = SalesOrderRules.DefaultAppHoldHours;

    public int ChargeDays { get; set; } = SalesOrderRules.DefaultChargeDays;

    public int AppChargeDays { get; set; } = SalesOrderRules.MaxAppChargeDays;

    public decimal AppMaxLineQty { get; set; } = SalesOrderRules.DefaultAppMaxLineQty;

    public decimal AppMaxOrderQty { get; set; } = SalesOrderRules.DefaultAppMaxOrderQty;

    public int AppUnpaidAlertCount { get; set; } = SalesOrderRules.DefaultAppUnpaidAlertCount;

    /// <summary>Forma de pagamento do tipo <c>BankSlip</c> que a venda do pedido pago por boleto usa (CA 16).</summary>
    public Guid? BankSlipPaymentMethodId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
