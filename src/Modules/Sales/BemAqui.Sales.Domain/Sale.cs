using BemAqui.Sales.Contracts;
using BemAqui.SharedKernel;

namespace BemAqui.Sales.Domain;

/// <summary>
/// Venda (<c>sales.sales</c>, VND-01): documento com movimento, nunca apagado, só cancelado. O id vem de quem entrega (no PDV, nasce no
/// aparelho) e é a chave de idempotência. Canal sem caixa tem <see cref="RegisterId"/> e <see cref="CashSessionId"/> nulos. Auditada:
/// a criação e o cancelamento ficam no histórico (o registro de eventos do PDV é a trilha das vendas de balcão, rascunho F).
/// </summary>
[DocumentName("A venda")]
public sealed class Sale : Entity, IAuditable, IConcurrencyVersioned
{
    public string Number { get; set; } = string.Empty;

    public SaleChannel Channel { get; init; }

    public SaleStatus Status { get; private set; } = SaleStatus.Open;

    public SaleStockStatus StockStatus { get; set; } = SaleStockStatus.Pending;

    /// <summary>Documento fiscal da venda (rascunho E): nulo até o Fiscal reagir; esta story não emite.</summary>
    public string? FiscalStatus { get; set; }

    public Guid? InvoiceId { get; set; }

    /// <summary>Loja (Pos, PDV-01); nula até existirem lojas ou no canal que não tem loja.</summary>
    public Guid? StoreId { get; init; }

    public Guid? RegisterId { get; init; }

    public Guid? CashSessionId { get; init; }

    /// <summary>Local de estoque de onde a venda sai (o da loja; sem loja, o local padrão no momento da entrega).</summary>
    public Guid? StockLocationId { get; init; }

    /// <summary>Cliente (terceiro); nulo = consumidor final.</summary>
    public Guid? CustomerId { get; init; }

    public Guid? SellerUserId { get; init; }

    public string SourceType { get; init; } = string.Empty;

    public Guid? SourceId { get; init; }

    public decimal Subtotal { get; private set; }

    public decimal DiscountTotal { get; private set; }

    public decimal ServiceFee { get; private set; }

    public decimal Total { get; private set; }

    /// <summary>Capturado líquido do troco − total, gravado quando a venda do aparelho chega com divergência (CA 5); nulo quando bate.</summary>
    public decimal? PaymentDifference { get; set; }

    public bool FromDevice { get; init; }

    public long? PriceListVersion { get; init; }

    public DateTimeOffset? CompletedAtDevice { get; init; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Data de negócio (America/Sao_Paulo) da conclusão: agrupa recebível e receita por dia.</summary>
    public DateOnly BusinessDate { get; set; }

    public DateTimeOffset ReceivedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public Guid? CancelledBy { get; private set; }

    public string? CancelReason { get; private set; }

    public uint Version { get; set; }

    public static Sale Create(Guid id, SaleChannel channel, string number, string sourceType, Guid? sourceId, Guid? storeId, Guid? registerId, Guid? cashSessionId,
        Guid? stockLocationId, Guid? customerId, Guid? sellerUserId, bool fromDevice, long? priceListVersion, DateTimeOffset? completedAtDevice, DateTimeOffset receivedAt, Guid? createdBy) =>
        new()
        {
            Id = id, Channel = channel, Number = number, SourceType = sourceType, SourceId = sourceId, StoreId = storeId, RegisterId = registerId, CashSessionId = cashSessionId,
            StockLocationId = stockLocationId, CustomerId = customerId, SellerUserId = sellerUserId, FromDevice = fromDevice, PriceListVersion = priceListVersion,
            CompletedAtDevice = completedAtDevice, ReceivedAt = receivedAt, CreatedBy = createdBy,
        };

    /// <summary>Totais a partir das linhas: subtotal = Σ preço × quantidade; desconto = Σ desconto; total = subtotal − desconto + taxa de serviço.</summary>
    public void ApplyTotals(IEnumerable<SaleLine> lines, decimal serviceFee)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var list = lines.ToList();
        Subtotal = SaleRules.Money(list.Sum(l => l.Gross));
        DiscountTotal = SaleRules.Money(list.Sum(l => l.Discount));
        ServiceFee = SaleRules.Money(serviceFee);
        Total = SaleRules.Money(Subtotal - DiscountTotal + ServiceFee);
    }

    /// <summary>Situação pelo que foi capturado: nada = aberta; parte = parcialmente paga; tudo = concluída (uma vez só).</summary>
    public bool ApplyPayments(decimal capturedNet, DateTimeOffset at, DateOnly businessDate)
    {
        if (Status is SaleStatus.Completed or SaleStatus.Cancelled or SaleStatus.PartiallyReturned or SaleStatus.Returned)
        {
            return false;
        }

        if (capturedNet >= Total)
        {
            Status = SaleStatus.Completed;
            CompletedAt = at;
            BusinessDate = businessDate;
            return true;
        }

        Status = capturedNet > 0 ? SaleStatus.PartiallyPaid : SaleStatus.Open;
        return false;
    }

    /// <summary>Venda entregue já concluída (CA 3): conclui mesmo com divergência, quando vem do aparelho (CA 5).</summary>
    public void Complete(DateTimeOffset at, DateOnly businessDate)
    {
        Status = SaleStatus.Completed;
        CompletedAt = at;
        BusinessDate = businessDate;
    }

    /// <summary>PDV-06 CA 6: devolução concluída; a venda fica parcialmente devolvida ou devolvida (continua feita: nada é apagado nem estornado aqui).</summary>
    public void MarkReturned(SaleStatus status)
    {
        if (status is not (SaleStatus.PartiallyReturned or SaleStatus.Returned) || !ReturnRules.Done.Contains(Status))
        {
            throw new InvalidOperationException("Só a venda feita passa a devolvida.");
        }

        Status = status;
    }

    public void Cancel(string reason, Guid? by, DateTimeOffset at)
    {
        Status = SaleStatus.Cancelled;
        CancelReason = reason;
        CancelledBy = by;
        CancelledAt = at;
    }
}

/// <summary>Linha da venda (<c>sales.sale_lines</c>): preço e desconto gravados; <see cref="UnitCost"/> (<c>decimal(18,6)</c>) preenchido na baixa.</summary>
public sealed class SaleLine : Entity
{
    public Guid SaleId { get; init; }

    public int LineNumber { get; init; }

    public Guid ProductId { get; init; }

    /// <summary>Produto pai da grade (a linha vende a variação); nulo fora de grade.</summary>
    public Guid? ParentProductId { get; init; }

    /// <summary>Item de estoque (<c>inventory.items</c>) ligado ao produto; nulo em serviço, que não baixa estoque.</summary>
    public Guid? ItemId { get; init; }

    public string Description { get; init; } = string.Empty;

    public string UnitCode { get; init; } = string.Empty;

    public decimal Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal Discount { get; init; }

    public decimal Total { get; init; }

    [NotAudited]
    public decimal? UnitCost { get; set; }

    public string? Note { get; init; }

    public string? Seat { get; init; }

    /// <summary>PDV-04 CA 3: o preço da tabela quando o praticado (<see cref="UnitPrice"/>) foi trocado no caixa; nulo sem troca.</summary>
    public decimal? ListUnitPrice { get; init; }

    /// <summary>PDV-04 CA 1: o motivo do desconto (da lista do caixa, ou "Outro" com o texto). O desconto no total chega rateado nas linhas.</summary>
    public string? DiscountReason { get; init; }

    /// <summary>PDV-04 CA 3: o motivo da troca de preço.</summary>
    public string? PriceReason { get; init; }

    public const int MaxReasonLength = 140;

    public decimal Gross => SaleRules.Money(Quantity * UnitPrice);

    public static SaleLine Create(Guid id, Guid saleId, int lineNumber, Guid productId, Guid? parentProductId, Guid? itemId, string description, string unitCode, decimal quantity,
        decimal unitPrice, decimal discount, string? note, string? seat, decimal? listUnitPrice = null, string? discountReason = null, string? priceReason = null) =>
        new()
        {
            Id = id, SaleId = saleId, LineNumber = lineNumber, ProductId = productId, ParentProductId = parentProductId, ItemId = itemId, Description = description, UnitCode = unitCode,
            Quantity = quantity, UnitPrice = unitPrice, Discount = discount, Total = SaleRules.Money((quantity * unitPrice) - discount), Note = note, Seat = seat,
            ListUnitPrice = listUnitPrice, DiscountReason = discountReason, PriceReason = priceReason,
        };
}

/// <summary>Parte da venda (<c>sales.sale_parts</c>): split por pessoa; venda sem split tem uma parte só.</summary>
public sealed class SalePart : Entity
{
    public Guid SaleId { get; init; }

    public string Label { get; init; } = string.Empty;

    public SalePartKind Kind { get; init; }

    public decimal Amount { get; init; }

    public bool Paid { get; set; }

    public static SalePart Create(Guid id, Guid saleId, string label, SalePartKind kind, decimal amount) =>
        new() { Id = id, SaleId = saleId, Label = label, Kind = kind, Amount = amount };
}

/// <summary>
/// Pagamento (<c>sales.sale_payments</c>, "tender", rascunho C). Os campos de TEF e Pix existem desde já, vazios no manual. <b>Nunca</b> número
/// de cartão, validade, código de segurança ou trilha (CA 8, teste de arquitetura): do cartão ficam bandeira, NSU, autorização e a referência
/// do provedor. A situação só muda pela <see cref="PaymentStateMachine"/>.
/// </summary>
public sealed class SalePayment : Entity
{
    public Guid SaleId { get; init; }

    public Guid SalePartId { get; init; }

    public Guid PaymentMethodId { get; init; }

    public decimal Amount { get; init; }

    public decimal ChangeAmount { get; init; }

    public SalePaymentStatus Status { get; private set; } = SalePaymentStatus.Created;

    public PaymentIntegration Integration { get; init; }

    public int Installments { get; init; } = 1;

    public string? Provider { get; init; }

    public Guid? AcquirerPartyId { get; init; }

    public string? CardBrand { get; init; }

    public string? Nsu { get; init; }

    public string? AuthorizationCode { get; init; }

    public string? Txid { get; init; }

    public string? EndToEndId { get; init; }

    /// <summary>Quem confirmou (no manual, o operador que atestou na maquininha).</summary>
    public Guid? ConfirmedBy { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    /// <summary>Pix atestado pelo operador já conferido contra o extrato (CX-02).</summary>
    public DateTimeOffset? StatementCheckedAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public decimal NetAmount => Amount - ChangeAmount;

    public static SalePayment Create(Guid id, Guid saleId, Guid partId, Guid paymentMethodId, decimal amount, decimal changeAmount, PaymentIntegration integration, int installments,
        string? provider, Guid? acquirerPartyId, string? cardBrand, string? nsu, string? authorizationCode, string? txid, string? endToEndId, DateTimeOffset createdAt) =>
        new()
        {
            Id = id, SaleId = saleId, SalePartId = partId, PaymentMethodId = paymentMethodId, Amount = amount, ChangeAmount = changeAmount, Integration = integration,
            Installments = installments, Provider = provider, AcquirerPartyId = acquirerPartyId, CardBrand = cardBrand, Nsu = nsu, AuthorizationCode = authorizationCode, Txid = txid,
            EndToEndId = endToEndId, CreatedAt = createdAt,
        };

    /// <summary>Muda a situação pela máquina de estados; transição proibida devolve falso e nada muda.</summary>
    public bool MoveTo(SalePaymentStatus next, Guid? by, DateTimeOffset at)
    {
        if (!PaymentStateMachine.CanMove(Status, next))
        {
            return false;
        }

        Status = next;
        if (next == SalePaymentStatus.Captured)
        {
            ConfirmedBy = by;
            ConfirmedAt = at;
        }

        return true;
    }

    /// <summary>Pagamento entregue já num estado (venda concluída no canal): percorre o caminho válido de <c>Created</c> até ele.</summary>
    public bool Arrive(SalePaymentStatus status, Guid? by, DateTimeOffset at) =>
        PaymentStateMachine.PathTo(status) is { } path && path.All(step => MoveTo(step, by, at));
}

/// <summary>Situação da baixa de uma linha agregada por item: baixada, sem saldo, bloqueada (item com lote ou série) ou estornada.</summary>
public enum SaleStockPostingStatus
{
    Posted,
    Shortfall,
    Blocked,
    Reversed,
}

/// <summary>
/// Baixa de estoque da venda (<c>sales.stock_postings</c>): uma por venda e item, com o movimento do estoque e o custo unitário
/// (<c>decimal(18,6)</c>). No PDV a baixa também é por venda (SPK-03, item 7) e nunca recusa: sai o que há em saldo (<see cref="StockMovementId"/>)
/// e o restante (<see cref="ShortfallQuantity"/>) fica numa falta aberta no Estoque até alguém aplicar a baixa (PDV-05, opção B da PP-02).
/// </summary>
public sealed class SaleStockPosting : Entity
{
    public Guid SaleId { get; init; }

    public Guid ItemId { get; init; }

    public Guid LocationId { get; init; }

    public decimal Quantity { get; init; }

    /// <summary>PDV-05: a parte da quantidade que ainda não saiu do estoque (falta aberta); zero quando tudo baixou.</summary>
    public decimal ShortfallQuantity { get; set; }

    public decimal? UnitCost { get; set; }

    public Guid? StockMovementId { get; set; }

    public Guid? ReversalMovementId { get; set; }

    public SaleStockPostingStatus Status { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
}
