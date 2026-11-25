using System.Text.Json.Serialization;
using BemAqui.Sales.Contracts;

namespace BemAqui.Sales.Application;

/// <summary>Estado procurado na lista de vendas (PDV-07 CA 2): situação da venda ou um dos chips (falta de saldo, divergência, feita sem internet).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SaleListState>))]
public enum SaleListState
{
    Completed,
    Cancelled,
    Returned,
    StockShortfall,
    Divergence,
    Offline,
}

/// <summary>Filtro da lista de vendas (PDV-07 CA 2): período em datas de negócio (São Paulo), loja, caixa, operador, forma de pagamento e estado.</summary>
public sealed record SaleListFilter(DateOnly From, DateOnly To, Guid? StoreId = null, Guid? RegisterId = null, Guid? OperatorId = null, Guid? PaymentMethodId = null,
    SaleListState? State = null);

/// <summary>
/// Uma venda na lista (PDV-07 CA 1). <c>CustomerDocument</c> vem sempre mascarado (CA 6). <c>Cost</c> e <c>Margin</c> só com <c>sales.view_cost</c>: sem
/// ela, ficam fora da resposta (CA 5); com ela e sem o custo de alguma linha (baixa pendente), também ficam fora, porque o custo seria parcial.
/// </summary>
public sealed record SaleListItem(
    Guid Id,
    string Number,
    DateTimeOffset At,
    DateOnly BusinessDate,
    SaleChannel Channel,
    Guid? StoreId,
    string? StoreName,
    Guid? RegisterId,
    string? RegisterLabel,
    Guid? OperatorId,
    string? OperatorName,
    decimal Total,
    IReadOnlyList<string> PaymentMethods,
    SaleStatus Status,
    SaleStockStatus StockStatus,
    bool HasDivergence,
    bool Offline,
    string? CustomerDocument,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Cost = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Margin = null);

/// <summary>
/// Totais do filtro inteiro, não da página (PDV-07 regra): quantidade de vendas, o total das feitas (concluídas e devolvidas), as canceladas à parte e,
/// com <c>sales.view_cost</c>, o custo e a margem das feitas.
/// </summary>
public sealed record SaleListTotals(
    int Count,
    decimal Total,
    int CancelledCount,
    decimal CancelledTotal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Cost = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Margin = null);

/// <summary>Página da lista de vendas no formato do <c>PagedList</c> (items, page, pageSize, total), com os totais do filtro.</summary>
public sealed record SaleListPage(IReadOnlyList<SaleListItem> Items, int Page, int PageSize, int Total, SaleListTotals Totals);

/// <summary>Linha da venda no detalhe (CA 4): preço da tabela (quando houve troca) e o praticado, desconto e os motivos. Custo só com <c>sales.view_cost</c>.</summary>
public sealed record SaleDetailLine(
    Guid Id,
    int LineNumber,
    string Description,
    string UnitCode,
    decimal Quantity,
    decimal ListUnitPrice,
    decimal UnitPrice,
    decimal Discount,
    decimal Total,
    string? DiscountReason,
    string? PriceReason,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? UnitCost = null);

/// <summary>Pagamento no detalhe: forma, valor, troco, situação e, do cartão, só bandeira, NSU e autorização.</summary>
public sealed record SaleDetailPayment(
    Guid Id,
    string MethodName,
    decimal Amount,
    decimal ChangeAmount,
    SalePaymentStatus Status,
    PaymentIntegration Integration,
    int Installments,
    string? CardBrand,
    string? Nsu,
    string? AuthorizationCode,
    DateTimeOffset? ConfirmedAt,
    string? ConfirmedByName);

/// <summary>
/// Um passo da trilha da venda (CA 4), em ordem de tempo. <c>Kind</c>: <c>device</c> (evento do aparelho), <c>approval</c> (supervisor), <c>sync</c>
/// (sincronização), <c>stock</c>, <c>finance</c>, <c>print</c> (impressão e reimpressão), <c>pending</c> (pendência do PDV) ou <c>sale</c> (cancelamento).
/// <c>Tone</c>: <c>ok</c>, <c>warn</c>, <c>crit</c> ou <c>info</c>.
/// </summary>
public sealed record SaleTrailEntry(DateTimeOffset At, string Kind, string Title, string? Detail, string Tone);

/// <summary>Venda completa para a retaguarda (CA 4 a 6). O documento do consumidor vem mascarado; o inteiro é a rota auditada à parte.</summary>
public sealed record SaleDetail(
    Guid Id,
    string Number,
    SaleChannel Channel,
    SaleStatus Status,
    SaleStockStatus StockStatus,
    bool HasDivergence,
    bool Offline,
    decimal? PaymentDifference,
    Guid? StoreId,
    string? StoreName,
    Guid? RegisterId,
    string? RegisterLabel,
    Guid? CashSessionId,
    Guid? OperatorId,
    string? OperatorName,
    DateOnly BusinessDate,
    DateTimeOffset At,
    DateTimeOffset? CompletedAtDevice,
    DateTimeOffset ReceivedAt,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal ServiceFee,
    decimal Total,
    string? CustomerDocument,
    DateTimeOffset? CancelledAt,
    string? CancelReason,
    string? CancelledByName,
    IReadOnlyList<SaleDetailLine> Lines,
    IReadOnlyList<SaleDetailPayment> Payments,
    IReadOnlyList<SaleTrailEntry> Trail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Cost = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? Margin = null);

/// <summary>O documento inteiro do consumidor da venda (CA 6), lido com auditoria.</summary>
public sealed record SaleCustomerDocumentView(string Document);
