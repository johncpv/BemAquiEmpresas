using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Sales.Contracts;

/// <summary>Situação da devolução (PDV-06, 04 seção 6): pedida, aprovada, concluída ou recusada. Documento nunca apagado.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SaleReturnStatus>))]
public enum SaleReturnStatus
{
    Requested,
    Approved,
    Completed,
    Rejected,
}

/// <summary>Forma do reembolso da devolução (PDV-06 CA 5): vale-troca (o padrão), dinheiro (com supervisor) ou estorno no cartão (com o NSU).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RefundKind>))]
public enum RefundKind
{
    StoreCredit,
    Cash,
    CardReversal,
}

/// <summary>
/// Devolução concluída (outbox, PDV-06): a venda original mudou de situação, o estoque já recebeu a entrada ou a perda (na mesma transação) e o
/// reembolso aconteceu. Só ids e valores (ADR 0037 §2). O Financeiro lança o vale-troca como passivo e reduz o título da adquirente no estorno de
/// cartão; o dinheiro sai pelo movimento de caixa (<c>CashMovementRecorded</c>, tipo <c>Refund</c>).
/// </summary>
public sealed record SaleReturned(
    Guid ReturnId,
    string Number,
    Guid SaleId,
    Guid? StoreId,
    Guid? CashSessionId,
    Guid? OriginalCashSessionId,
    RefundKind RefundKind,
    decimal Amount,
    Guid? StoreCreditId,
    Guid? CardPaymentMethodId,
    DateOnly BusinessDate,
    DateTimeOffset CompletedAt) : IIntegrationEvent;

/// <summary>Abatimento de vale-troca feito com rede para um pagamento do caixa (PDV-06 CA 8): a venda, o valor e se foi desfeito.</summary>
public sealed record StoreCreditRedemptionRef(Guid TenderId, Guid SaleId, Guid StoreCreditId, decimal Amount, bool Reversed);

/// <summary>
/// Abatimentos de vale-troca para a subida da venda do caixa (PDV-06 CA 8): o pagamento do tipo vale-troca só entra com o abatimento feito no servidor
/// (mesmo id do pagamento, mesma venda e mesmo valor); sem ele, a venda vai para a quarentena.
/// </summary>
public interface IStoreCreditRedemptions
{
    Task<StoreCreditRedemptionRef?> FindAsync(Guid tenderId, CancellationToken cancellationToken = default);
}
