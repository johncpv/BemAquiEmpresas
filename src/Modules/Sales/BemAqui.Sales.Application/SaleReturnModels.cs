using System.Text.Json.Serialization;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;

namespace BemAqui.Sales.Application;

/// <summary>
/// Localizar a venda para devolver (PDV-06 CA 1): pelo número do cupom ou pelo CPF/CNPJ do cliente, sempre no corpo (nunca na URL). O documento nunca
/// vai para log.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaleLookupInput(string? Number, string? Document)
{
    public override string ToString() => $"SaleLookupInput {{ Number = {Number}, Document = {(Document is null ? "null" : "***")} }}";
}

/// <summary>Linha da venda para devolver: vendido, já devolvido, o que ainda pode voltar (CA 2) e o valor praticado (com o desconto rateado).</summary>
public sealed record ReturnCandidateLine(Guid SaleLineId, string Description, string UnitCode, decimal Sold, decimal Returned, decimal Returnable, decimal LineTotal, decimal UnitAmount);

/// <summary>Pagamento da venda que aceita estorno no cartão: o que ainda pode ser estornado.</summary>
public sealed record ReturnCandidatePayment(Guid PaymentId, Guid PaymentMethodId, string? CardBrand, decimal Amount, decimal Reversible);

/// <summary>Venda localizada: o prazo da loja (CA 3) e, fora dele, a mensagem.</summary>
public sealed record ReturnCandidate(
    Guid SaleId, string Number, DateTimeOffset? CompletedAt, DateOnly BusinessDate, decimal Total, SaleStatus Status, DateOnly Deadline, bool Late, string? LateMessage,
    IReadOnlyList<ReturnCandidateLine> Lines, IReadOnlyList<ReturnCandidatePayment> CardPayments);

public sealed record SaleLookupResult(IReadOnlyList<ReturnCandidate> Sales, bool CashSessionOpen);

/// <summary>Item devolvido: a linha da venda, a quantidade, o destino (CA 4) e o motivo.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaleReturnLineInput(Guid SaleLineId, decimal Quantity, ReturnDestination Destination, string Reason);

/// <summary>
/// Pedido de devolução (CA 2 a 5 e 9): os itens, a forma do reembolso (vale-troca é o padrão), o pagamento do cartão e o NSU no estorno e a troca. O
/// cliente do vale é sempre o da venda original (revisão de segurança, achado 9).
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnRequestInput(
    Guid SaleId, IReadOnlyList<SaleReturnLineInput> Lines, RefundKind RefundKind = RefundKind.StoreCredit, Guid? CardPaymentId = null, string? CardNsu = null, bool Exchange = false,
    string? Note = null);

/// <summary>Aprovar e concluir: com a aprovação do supervisor quando a devolução pede (dinheiro ou fora do prazo).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ReturnApproveInput(Guid? SupervisorApprovalId = null);

public sealed record SaleReturnLineView(Guid Id, Guid SaleLineId, string Description, decimal Quantity, decimal Amount, ReturnDestination Destination, string Reason, string? StockNote);

/// <summary>
/// Vale emitido (CA 7). <c>Code</c> só vem na resposta da conclusão (para imprimir): o servidor guarda só o hash e os 4 últimos caracteres.
/// </summary>
public sealed record StoreCreditIssued(Guid Id, string? Code, string CodeLast4, decimal Amount, decimal Balance, DateOnly ExpiresOn)
{
    public override string ToString() => $"StoreCreditIssued {{ Id = {Id}, Code = {(Code is null ? "null" : "***")}, CodeLast4 = {CodeLast4}, Balance = {Balance}, ExpiresOn = {ExpiresOn} }}";
}

/// <summary>Revisão de segurança (achado 5): reemitir o código do vale-troca perdido, com a aprovação do supervisor (ação <c>CreditReissue</c>).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StoreCreditReissueInput(Guid SupervisorApprovalId);

public sealed record SaleReturnView(
    Guid Id, string Number, Guid SaleId, string SaleNumber, SaleReturnStatus Status, RefundKind RefundKind, decimal Total, bool Late, DateOnly Deadline, string? LateMessage,
    bool NeedsSupervisor, bool IsExchange, string? CardNsu, IReadOnlyList<SaleReturnLineView> Lines, StoreCreditIssued? StoreCredit, DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt);

/// <summary>Consultar o vale-troca (CA 8): o código vai no corpo (valor ao portador, nunca na URL).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StoreCreditLookupInput(string Code)
{
    public override string ToString() => "StoreCreditLookupInput { Code = *** }";
}

/// <summary>O vale consultado: saldo, validade e, quando não serve, a mensagem ("Vale vencido em ...", "Vale sem saldo.").</summary>
public sealed record StoreCreditView(Guid Id, string CodeLast4, decimal Amount, decimal Balance, DateOnly ExpiresOn, bool Usable, string? Problem);

/// <summary>Usar o vale num pagamento da venda do caixa (CA 8): o id do pagamento do aparelho torna o uso idempotente.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record StoreCreditRedeemInput(string Code, Guid SaleId, Guid TenderId, decimal Amount)
{
    public override string ToString() => $"StoreCreditRedeemInput {{ SaleId = {SaleId}, TenderId = {TenderId}, Amount = {Amount} }}";
}

public sealed record StoreCreditRedeemed(Guid StoreCreditId, Guid TenderId, decimal Amount, decimal Balance, DateOnly ExpiresOn);

/// <summary>Filtro do relatório de devoluções (CA 12): período em datas de negócio e loja.</summary>
public sealed record SaleReturnFilter(DateOnly From, DateOnly To, Guid? StoreId = null);

public sealed record SaleReturnReportRow(
    Guid Id, string Number, DateTimeOffset CompletedAt, Guid StoreId, string StoreName, Guid OperatorId, string OperatorName, string SaleNumber, RefundKind RefundKind,
    string RefundLabel, string Reasons, decimal Total, bool IsExchange);

/// <summary>Contagem e valor por operador, motivo ou forma de reembolso.</summary>
public sealed record SaleReturnReportGroup(string Key, string Label, int Count, decimal Amount);

public sealed record SaleReturnReport(
    IReadOnlyList<SaleReturnReportRow> Rows, IReadOnlyList<SaleReturnReportGroup> ByOperator, IReadOnlyList<SaleReturnReportGroup> ByReason,
    IReadOnlyList<SaleReturnReportGroup> ByRefundKind, decimal Total, bool Truncated);

public sealed record SaleReturnFile(byte[] Content, string FileName);
