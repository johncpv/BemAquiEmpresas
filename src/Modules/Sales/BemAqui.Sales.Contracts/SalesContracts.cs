using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Sales.Contracts;

/// <summary>
/// Canal da venda (VND-01, <c>sales.sales.channel</c>). Só o <see cref="Pos"/> tem caixa e sessão de caixa; os demais canais vendem sem caixa
/// (loja virtual, plataforma, OS) e entram pelo mesmo contrato.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SaleChannel>))]
public enum SaleChannel
{
    Pos,
    Online,
    Delivery,
    Table,
    ServiceOrder,
}

/// <summary>
/// Situação da venda: aberta (sem pagamento), parcialmente paga, concluída ou cancelada. Venda nunca é apagada. PDV-06: a concluída passa a
/// parcialmente devolvida ou devolvida com a devolução concluída; continua sendo venda feita (receita, recebível e baixa ficam).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SaleStatus>))]
public enum SaleStatus
{
    Open,
    PartiallyPaid,
    Completed,
    Cancelled,
    PartiallyReturned,
    Returned,
}

/// <summary>
/// Máquina de estados única do pagamento (rascunho C §1): <c>Created → Pending → Captured → (Voided | Refunded)</c>, mais <c>Failed</c> e
/// <c>Unknown</c> (queda entre autorizar e confirmar no TEF). Pagamento capturado é fato: só sai por estorno.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalePaymentStatus>))]
public enum SalePaymentStatus
{
    Created,
    Pending,
    Captured,
    Voided,
    Refunded,
    Failed,
    Unknown,
}

/// <summary>Como o pagamento foi feito: manual (maquininha externa, atestado pelo operador), TEF, SmartPOS, Pix por API ou gateway.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentIntegration>))]
public enum PaymentIntegration
{
    Manual,
    Tef,
    SmartPos,
    PixApi,
    Gateway,
}

/// <summary>Divisão da conta (split): partes iguais, por valor ou por itens. Venda sem split tem uma parte só.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SalePartKind>))]
public enum SalePartKind
{
    Equal,
    Amount,
    Items,
}

/// <summary>Situação da baixa de estoque da venda: pendente, baixada, com falta de saldo, sem estoque (só serviço) ou estornada.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SaleStockStatus>))]
public enum SaleStockStatus
{
    Pending,
    Posted,
    Shortfall,
    NotRequired,
    Reversed,
}

/// <summary>Linha entregue pelo canal: produto vendável do catálogo (numa grade, a própria variação), quantidade (<c>decimal(18,4)</c>), preço e desconto (<c>decimal(18,2)</c>).</summary>
/// <remarks>PDV-04: <c>ListUnitPrice</c> é o preço da tabela quando o praticado mudou (troca de preço); os motivos do desconto e da troca, do caixa.</remarks>
public sealed record SaleLineInput(
    Guid Id, Guid ProductId, decimal Quantity, decimal UnitPrice, decimal Discount, string? Note = null, string? Seat = null, decimal? ListUnitPrice = null, string? DiscountReason = null,
    string? PriceReason = null);

/// <summary>Parte da venda (split por pessoa). Sem partes, a venda ganha uma parte única com o total.</summary>
public sealed record SalePartInput(Guid Id, string Label, SalePartKind Kind, decimal Amount);

/// <summary>
/// Pagamento entregue pelo canal. Do cartão só ficam bandeira, NSU, código de autorização e a referência do provedor (CA 8): não existe campo
/// para número, validade, código de segurança nem trilha, e os campos livres recusam sequência que pareça número de cartão.
/// <c>Amount</c> é o valor entregue; <c>ChangeAmount</c>, o troco (só em forma que dá troco). <c>ConfirmedBy</c>: quem atestou
/// (no caixa, o operador da sessão por PIN conferido na subida da OFF-02); nulo = o usuário do request.
/// </summary>
public sealed record SalePaymentInput(
    Guid Id,
    Guid PaymentMethodId,
    decimal Amount,
    SalePaymentStatus Status,
    PaymentIntegration Integration,
    decimal ChangeAmount = 0,
    Guid? PartId = null,
    int Installments = 1,
    string? Provider = null,
    Guid? AcquirerPartyId = null,
    string? CardBrand = null,
    string? Nsu = null,
    string? AuthorizationCode = null,
    string? Txid = null,
    string? EndToEndId = null,
    Guid? ConfirmedBy = null);

/// <summary>
/// Venda concluída entregue por um módulo (VND-01 CA 3). <c>SaleId</c> é a chave de idempotência (no PDV, nasce no aparelho). <c>Number</c> é o
/// número do canal; <c>SourceType</c>/<c>SourceId</c>, o documento de origem (pedido online, OS, lote do PDV). <c>FromDevice</c> marca a venda
/// feita no aparelho do PDV: ela nunca é recusada por divergência de pagamento (CA 5, OFF-02). <c>StockLocationId</c> nulo = local padrão.
/// </summary>
public sealed record SaleIngestionRequest(
    Guid SaleId,
    SaleChannel Channel,
    string Number,
    string SourceType,
    Guid? SourceId,
    DateTimeOffset CompletedAt,
    IReadOnlyList<SaleLineInput> Lines,
    IReadOnlyList<SalePaymentInput> Payments,
    IReadOnlyList<SalePartInput>? Parts = null,
    Guid? StoreId = null,
    Guid? RegisterId = null,
    Guid? CashSessionId = null,
    Guid? StockLocationId = null,
    Guid? CustomerId = null,
    Guid? SellerUserId = null,
    decimal ServiceFee = 0,
    bool FromDevice = false,
    DateTimeOffset? CompletedAtDevice = null,
    long? PriceListVersion = null);

/// <summary>Como a venda entrou: aceita, aceita com divergência de pagamento (só do aparelho do PDV).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SaleIngestionOutcome>))]
public enum SaleIngestionOutcome
{
    Accepted,
    AcceptedWithDivergence,
}

/// <summary>
/// Resposta da ingestão (CA 3 e 4). A segunda entrega da mesma venda devolve a mesma resposta, com <c>Replayed</c> verdadeiro.
/// <c>PaymentDifference</c> = capturado líquido do troco − total (zero quando bate).
/// </summary>
public sealed record SaleIngestionResult(Guid SaleId, string Number, SaleStatus Status, decimal Total, decimal CapturedTotal, decimal PaymentDifference, SaleIngestionOutcome Outcome, bool Replayed = false);

/// <summary>
/// Cancelamento da venda (rascunho C §7). Venda com pagamento capturado exige o estorno de cada um: no manual, quem cancela confirma que estornou
/// na maquininha (<c>CapturedRefundedOutside</c>). Nada é apagado.
/// </summary>
public sealed record SaleCancellationRequest(Guid SaleId, string Reason, bool CapturedRefundedOutside, Guid? CancelledBy = null);

/// <summary>
/// Entrada das vendas no núcleo (VND-01, ADR das fronteiras: 02, seção 4.1). Grava venda, linhas, partes e pagamentos <b>na transação de quem
/// chama</b> e põe o <see cref="SaleCompleted"/> no outbox; o <c>Sales</c> é o único dono das tabelas <c>sales.*</c>.
/// </summary>
public interface ISaleIngestion
{
    Task<Result<SaleIngestionResult>> IngestAsync(SaleIngestionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Cancela a venda (auditado) e publica <see cref="SaleCancelled"/>: o estoque estorna a baixa e o financeiro refaz o recebível.</summary>
    Task<Result<SaleView>> CancelAsync(SaleCancellationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Pedido (comanda, OS, pedido online) que vira venda no primeiro pagamento (CA 7). <c>SaleId</c> é escolhido por quem chama.</summary>
public sealed record OrderSale(
    Guid SaleId,
    SaleChannel Channel,
    string Number,
    string SourceType,
    Guid SourceId,
    IReadOnlyList<SaleLineInput> Lines,
    Guid? StoreId = null,
    Guid? StockLocationId = null,
    Guid? CustomerId = null,
    Guid? SellerUserId = null,
    decimal ServiceFee = 0);

/// <summary>Um pagamento pedido para o pedido; passa pela máquina de estados no provedor da integração.</summary>
public sealed record OrderPaymentInput(
    Guid Id,
    Guid PaymentMethodId,
    decimal Amount,
    PaymentIntegration Integration = PaymentIntegration.Manual,
    decimal ChangeAmount = 0,
    string? PartLabel = null,
    int Installments = 1,
    string? Provider = null,
    Guid? AcquirerPartyId = null,
    string? CardBrand = null,
    string? Nsu = null,
    string? AuthorizationCode = null,
    string? Txid = null,
    string? EndToEndId = null);

public sealed record OrderPaymentRequest(OrderSale Order, IReadOnlyList<OrderPaymentInput> Payments);

/// <summary>
/// Pagamento de um pedido (CA 7): o <c>Sales</c> cria a venda (ou usa a já criada pelo mesmo <c>SaleId</c>), registra os pagamentos pela máquina
/// de estados e devolve a venda com eles. Quando o capturado fecha o total, a venda conclui e publica <see cref="SaleCompleted"/>. Pagamento
/// capturado é fato: se um pagante falha, a venda fica parcialmente paga, aberta.
/// </summary>
public interface IOrderPayments
{
    Task<Result<SaleView>> PayAsync(OrderPaymentRequest request, CancellationToken cancellationToken = default);
}

public sealed record SaleLineView(
    Guid Id, int LineNumber, Guid ProductId, Guid? ParentProductId, Guid? ItemId, string Description, string UnitCode, decimal Quantity, decimal UnitPrice, decimal Discount, decimal Total,
    decimal? UnitCost, string? Note, string? Seat, decimal? ListUnitPrice = null, string? DiscountReason = null, string? PriceReason = null);

public sealed record SalePartView(Guid Id, string Label, SalePartKind Kind, decimal Amount, bool Paid);

/// <summary>Pagamento como gravado: só bandeira, NSU, autorização e referência do provedor do cartão.</summary>
public sealed record SalePaymentView(
    Guid Id, Guid PartId, Guid PaymentMethodId, decimal Amount, decimal ChangeAmount, SalePaymentStatus Status, PaymentIntegration Integration, int Installments, string? Provider,
    Guid? AcquirerPartyId, string? CardBrand, string? Nsu, string? AuthorizationCode, string? Txid, string? EndToEndId, Guid? ConfirmedBy, DateTimeOffset? ConfirmedAt);

/// <summary>Venda para os outros módulos (<see cref="ISaleLookup"/>). <c>UnitCost</c> das linhas é o custo da baixa (nulo até baixar).</summary>
public sealed record SaleView(
    Guid Id,
    string Number,
    SaleChannel Channel,
    SaleStatus Status,
    SaleStockStatus StockStatus,
    Guid? StoreId,
    Guid? RegisterId,
    Guid? CashSessionId,
    Guid? StockLocationId,
    Guid? CustomerId,
    string SourceType,
    Guid? SourceId,
    DateOnly BusinessDate,
    DateTimeOffset? CompletedAt,
    decimal Subtotal,
    decimal DiscountTotal,
    decimal ServiceFee,
    decimal Total,
    decimal CapturedTotal,
    IReadOnlyList<SaleLineView> Lines,
    IReadOnlyList<SalePartView> Parts,
    IReadOnlyList<SalePaymentView> Payments,
    DateTimeOffset? CancelledAt = null,
    string? CancelReason = null);

/// <summary>
/// Pagamento capturado de venda concluída, líquido do troco (<c>Amount − ChangeAmount</c>), para o Financeiro montar o recebível por dia e canal
/// (CA 11). <c>AcquirerPartyId</c> é o do pagamento (o provedor que capturou), quando informado.
/// </summary>
public sealed record SalePaymentFact(Guid SaleId, Guid PaymentId, Guid PaymentMethodId, Guid? AcquirerPartyId, string? CardBrand, int Installments, decimal Amount, Guid? CustomerId);

/// <summary>Leitura da venda pelos outros módulos (Financeiro, Fiscal, retaguarda), sem tocar nas tabelas <c>sales.*</c>.</summary>
public interface ISaleLookup
{
    Task<SaleView?> FindAsync(Guid saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pagamentos capturados das vendas concluídas (não canceladas) de um dia de negócio e canal; <paramref name="cashSessionId"/> nulo = só as
    /// vendas sem sessão de caixa.
    /// </summary>
    Task<IReadOnlyList<SalePaymentFact>> CapturedPaymentsAsync(DateOnly businessDate, SaleChannel channel, Guid? cashSessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pagamentos capturados das vendas concluídas de uma sessão de caixa, com o dia de negócio de cada venda (CX-03: a taxa e o D+N são os
    /// vigentes na data da venda, e a sessão pode passar da meia-noite).
    /// </summary>
    Task<IReadOnlyList<SessionPaymentFact>> SessionPaymentsAsync(Guid cashSessionId, CancellationToken cancellationToken = default);
}

/// <summary>Pagamento capturado de venda concluída de uma sessão de caixa (CX-03), com o dia de negócio da venda.</summary>
public sealed record SessionPaymentFact(DateOnly BusinessDate, SalePaymentFact Payment);

/// <summary>
/// Receita de vendas concluídas de um dia, por loja e canal (CA 12): bruto (preço × quantidade), descontos, taxa de serviço, devoluções e líquido.
/// PDV-06 (revisão de segurança, achado 6): <c>Returns</c> são as devoluções concluídas no dia (data de negócio da devolução, valor dos itens devolvidos,
/// qualquer forma de reembolso); o líquido já vem sem elas (bruto − descontos + taxa − devoluções).
/// </summary>
public sealed record SalesRevenueLine(DateOnly Date, Guid? StoreId, SaleChannel Channel, int Sales, decimal Gross, decimal Discounts, decimal ServiceFees, decimal Net,
    decimal Returns = 0m);

/// <summary>Receita das vendas para o DRE por competência (CA 12): pela data de negócio da venda, sem as canceladas.</summary>
public interface ISalesRevenue
{
    Task<IReadOnlyList<SalesRevenueLine>> RevenueAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

/// <summary>
/// Venda concluída (outbox). Sem dado pessoal: o consumidor relê a venda pelo id. <c>CashSessionId</c> nulo = canal sem caixa (o estoque baixa
/// por venda e o financeiro agrupa por dia e canal); com sessão, o recebível é do fechamento do caixa (CX-03) e o estoque também baixa por venda, sem
/// nunca recusar (PDV-05: a falta vira divergência no Estoque).
/// </summary>
public sealed record SaleCompleted(Guid SaleId, string Number, SaleChannel Channel, Guid? StoreId, Guid? CashSessionId, DateOnly BusinessDate, decimal Total, DateTimeOffset CompletedAt)
    : IIntegrationEvent;

/// <summary>Venda cancelada (outbox): o estoque estorna a baixa e o financeiro refaz o recebível do dia e canal.</summary>
public sealed record SaleCancelled(Guid SaleId, string Number, SaleChannel Channel, Guid? StoreId, Guid? CashSessionId, DateOnly BusinessDate, decimal Total, DateTimeOffset CancelledAt)
    : IIntegrationEvent;

/// <summary>Documento do consumidor da venda do caixa (OFF-02 CA 17): <c>Kind</c> é <c>cpf</c> ou <c>cnpj</c>; <c>Value</c>, só para cifrar.</summary>
public sealed record SaleCustomerDocumentInput(Guid SaleId, string Kind, string Value)
{
    /// <summary>Nunca o documento em texto em log ou exceção.</summary>
    public override string ToString() => $"SaleCustomerDocumentInput {{ SaleId = {SaleId}, Kind = {Kind} }}";
}

/// <summary>
/// CPF/CNPJ do consumidor da venda (OFF-02 CA 17, PP-26), em registro à parte e apagável: cifrado e com índice cego, fora do registro de eventos
/// do caixa, do log e do outbox. O <c>Sales</c> é o dono de <c>sales.sale_customer_documents</c>; a gravação entra na transação de quem chama.
/// </summary>
public interface ISaleCustomerDocuments
{
    /// <summary>Grava o documento da venda (idempotente: a venda que já tem documento fica como está). Documento inválido: erro de validação.</summary>
    Task<Result> AttachAsync(SaleCustomerDocumentInput input, CancellationToken cancellationToken = default);

    /// <summary>As vendas com este documento, pelo índice cego (localizar a devolução, PP-26).</summary>
    Task<IReadOnlyList<Guid>> FindSalesAsync(string document, CancellationToken cancellationToken = default);
}

/// <summary>Vendas líquidas de uma forma de pagamento na sessão de caixa (pagamentos capturados − troco, sem as vendas canceladas).</summary>
public sealed record CashSessionMethodTotal(Guid PaymentMethodId, decimal Amount);

/// <summary>Pagamento manual (atestado pelo operador) de uma forma a conferir contra o extrato, na sessão (CX-02 CA 13).</summary>
public sealed record CashSessionManualPayment(Guid PaymentId, Guid SaleId, string SaleNumber, Guid PaymentMethodId, decimal Amount, DateTimeOffset? ConfirmedAt, Guid? ConfirmedBy);

/// <summary>
/// Resumo das vendas de uma sessão de caixa para o fechamento (CX-02): quantidade, canceladas, descontos, total, as feitas sem rede (chegaram ao
/// servidor mais de <see cref="CashSessionSalesSummary.OfflineAfter"/> depois de concluídas), o total por forma e os pagamentos manuais das formas
/// pedidas (Pix a conferir).
/// </summary>
public sealed record CashSessionSalesSummary(
    int Sales,
    int Cancelled,
    decimal CancelledTotal,
    decimal Discounts,
    decimal Total,
    int Offline,
    IReadOnlyList<CashSessionMethodTotal> ByMethod,
    IReadOnlyList<CashSessionManualPayment> ManualPayments)
{
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(5);
}

/// <summary>Vendas da sessão de caixa para o fechamento do PDV (CX-02): o <c>Pos</c> não lê <c>sales.*</c>; pergunta por aqui.</summary>
public interface ICashSessionSales
{
    Task<CashSessionSalesSummary> SummaryAsync(Guid cashSessionId, IReadOnlyCollection<Guid> reconcileMethodIds, CancellationToken cancellationToken = default);
}
