using System.Text.Json.Serialization;
using BemAqui.Finance.Contracts;
using BemAqui.Sales.Contracts;

namespace BemAqui.Sales.Application;

/// <summary>
/// Quem age no pedido (CA 2): sempre do principal autenticado, nunca do corpo. Pessoa = <see cref="SalesOrderOrigin.Backoffice"/> e o usuário; aplicativo
/// (ADR 0053, INT-01/INT-03) = a origem do aplicativo, o aplicativo (<see cref="SourceAppId"/>, INT-04 CA 6), o nome, quem autorizou
/// (<see cref="UserId"/>) e a conexão em uso (<see cref="ConnectionId"/>, só para a trilha).
/// </summary>
public sealed record SalesOrderActor(SalesOrderOrigin Origin, Guid? UserId, Guid? SourceAppId = null, string? AppName = null, Guid? ConnectionId = null)
{
    public bool IsApp => SourceAppId is not null;

    public static SalesOrderActor Person(Guid? userId) => new(SalesOrderOrigin.Backoffice, userId);

    /// <summary>Rótulo do autor na linha do tempo: "Integração {app}" (a pessoa que autorizou fica no id).</summary>
    public string? Label => IsApp ? "Integração " + (AppName ?? Origin.ToString()) : null;
}

/// <summary>Linha pedida: produto vendável, quantidade e desconto. O preço unitário, se vier, é ignorado (CA 5: sempre o da tabela).</summary>
public sealed record SalesOrderLineInput(Guid ProductId, decimal Quantity, decimal Discount = 0, decimal? UnitPrice = null);

/// <summary>Pedido novo (CA 1): cliente, linhas, desconto do pedido, local de estoque (nulo = o padrão), observação e, para aplicativo, o id externo.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesOrderInput(
    Guid CustomerId, IReadOnlyList<SalesOrderLineInput> Lines, decimal OrderDiscount = 0, Guid? StockLocationId = null, Guid? StoreId = null, string? Note = null,
    string? ExternalId = null);

/// <summary>Edição (CA 8): no rascunho e no aberto; <c>Version</c> é o <c>xmin</c> lido.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesOrderEditInput(
    Guid CustomerId, IReadOnlyList<SalesOrderLineInput> Lines, decimal OrderDiscount, Guid? StockLocationId, string? Note, uint Version);

/// <summary>"Confirmar pedido" (CA 9): com o preço mudado desde a criação, a primeira chamada responde 409 <c>price_changed</c> e a segunda aceita.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesOrderConfirmInput(uint Version, bool AcceptNewPrices = false);

/// <summary>"Gerar cobrança" (CA 15): vencimento (<c>date</c>) e se o boleto sai por e-mail.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesOrderChargeInput(DateOnly DueDate, bool SendEmail, uint Version);

/// <summary>"Registrar pagamento" (CA 20): a forma (dinheiro, maquininha, Pix recebido fora) e, no dinheiro, o valor entregue (troco).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesOrderPaymentInput(Guid PaymentMethodId, uint Version, decimal? AmountReceived = null, string? CardBrand = null, string? Nsu = null);

/// <summary>"Cancelar pedido" (CA 25) e "Cancelar pedido e estornar venda" (CA 26: <c>RefundedOutside</c> = "Já devolvi o valor ao cliente").</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesOrderCancelInput(string Reason, uint Version, bool RefundedOutside = false);

/// <summary>"Reabrir pedido" e "Devolver o valor" do pago depois de cancelado (CA 19).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesOrderVersionInput(uint Version, string? Reason = null);

/// <summary>Próximo passo do pedido (a lista agrupa por ele): confirmar, cobrar, aguardar pagamento, separar (VND-02b) ou nenhum.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesOrderNextStep>))]
public enum SalesOrderNextStep
{
    Confirm,
    Charge,
    AwaitPayment,
    Pick,
    None,
}

/// <summary>Faixa de problema do pedido (com a ação na tela).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalesOrderProblem>))]
public enum SalesOrderProblem
{
    DiscountNeedsApproval,
    ReservationShort,
    PaymentInReview,
    PaidAfterCancel,
}

public sealed record SalesOrderLineView(
    Guid Id, int LineNumber, Guid ProductId, Guid ItemId, string Description, string UnitCode, decimal Quantity, decimal UnitPrice, decimal Discount, decimal Total, decimal Reserved,
    long PriceListVersion);

/// <summary>Uma linha do tempo em linguagem simples (CA 29): estado de chegada, ação, quem, quando e o motivo.</summary>
public sealed record SalesOrderTimelineEntry(DateTimeOffset At, SalesOrderStatus? FromStatus, SalesOrderStatus ToStatus, string Action, string Actor, string? Reason);

public sealed record SalesOrderDetail(
    Guid Id,
    string Number,
    SalesOrderStatus Status,
    SalesOrderOrigin Origin,
    string? SourceAppName,
    string? ExternalId,
    string? AppLabel,
    Guid CustomerId,
    string CustomerName,
    Guid StockLocationId,
    string? StockLocationName,
    Guid? SaleId,
    Guid? ReceivableId,
    decimal Subtotal,
    decimal OrderDiscount,
    decimal DiscountTotal,
    decimal Total,
    DateTimeOffset? HoldUntil,
    string? Note,
    string? CancelReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    SalesOrderNextStep NextStep,
    IReadOnlyList<SalesOrderProblem> Problems,
    IReadOnlyList<SalesOrderLineView> Lines,
    OrderChargeView? Charge,
    IReadOnlyList<SalesOrderTimelineEntry> Timeline,
    uint Version,
    bool Replayed = false);

public sealed record SalesOrderListItem(
    Guid Id, string Number, SalesOrderStatus Status, SalesOrderOrigin Origin, string? SourceAppName, Guid CustomerId, string CustomerName, decimal Total, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, DateTimeOffset? HoldUntil, SalesOrderNextStep NextStep, IReadOnlyList<SalesOrderProblem> Problems, bool Late);

public sealed record SalesOrderStepCount(SalesOrderNextStep Step, int Count);

public sealed record SalesOrderListPage(IReadOnlyList<SalesOrderListItem> Items, int Total, IReadOnlyList<SalesOrderStepCount> Counts);

/// <summary>Filtros da lista (CA da tela): próximo passo, origem, aplicativo, cliente, período de criação, "parados há mais de N dias" e cancelados.</summary>
public sealed record SalesOrderListQuery(
    SalesOrderNextStep? Step, SalesOrderOrigin? Origin, Guid? SourceAppId, Guid? CustomerId, DateOnly? From, DateOnly? To, int? StaleDays, bool IncludeCancelled, string? Q);

/// <summary>Diálogo "Gerar cobrança" já preenchido (CA 15 a 17): valor, vencimento, e-mail, o que falta no cadastro e a forma do boleto.</summary>
public sealed record SalesOrderChargeDraft(
    decimal Amount, DateOnly DueDate, string? Email, bool IntegrationReady, bool PaymentMethodConfigured, bool CustomerBillable, IReadOnlyList<string> Missing, Guid CustomerId, uint Version);

/// <summary>Produto para a consulta rápida do pedido: preço da tabela padrão e o disponível no local padrão (físico − reservado).</summary>
public sealed record SalesOrderProductOption(Guid ProductId, string Description, string UnitCode, decimal? Price, decimal Available, string? Gtin);

public sealed record SalesOrderCustomerOption(Guid Id, string Name, string? Document);

/// <summary>Parâmetros de Configurações → Parâmetros → Pedidos de venda (padrões em <c>SalesOrderRules</c>).</summary>
public sealed record SalesOrderParametersView(
    decimal DiscountLimitPct, decimal AppDiscountLimitPct, int HoldHours, int AppHoldHours, int ChargeDays, int AppChargeDays, decimal AppMaxLineQty, decimal AppMaxOrderQty,
    int AppUnpaidAlertCount, Guid? BankSlipPaymentMethodId, Guid? Id, IReadOnlyList<SalesOrderMethodOption> BankSlipMethods);

public sealed record SalesOrderMethodOption(Guid Id, string Name);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesOrderParametersInput(
    decimal DiscountLimitPct, decimal AppDiscountLimitPct, int HoldHours, int AppHoldHours, int ChargeDays, int AppChargeDays, decimal AppMaxLineQty, decimal AppMaxOrderQty,
    int AppUnpaidAlertCount, Guid? BankSlipPaymentMethodId);

/// <summary>Contagem do card "Pedidos aguardando ação" do Início.</summary>
public sealed record SalesOrderSummaryView(int ToConfirm, int ToCharge, int AwaitingPayment, int WithProblem);

/// <summary>Forma para "Registrar pagamento" (dinheiro, maquininha, Pix recebido fora): só as ativas que não são boleto, fiado nem vale.</summary>
public sealed record SalesOrderPaymentOption(Guid Id, string Name, string Type, bool AllowsChange);
