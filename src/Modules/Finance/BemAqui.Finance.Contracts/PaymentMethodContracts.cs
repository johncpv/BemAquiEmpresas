using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Contracts;

/// <summary>
/// Tipo da forma de pagamento da venda (PAG-01 CA 1). Não confundir com <see cref="PaymentMethodKind"/>, que é como um título a pagar
/// é pago (Pix, boleto, TED).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PaymentMethodType>))]
public enum PaymentMethodType
{
    Cash,
    Pix,
    Debit,
    Credit,
    CreditInstallments,
    Voucher,
    StoreCredit,
    OnAccount,
}

/// <summary>Bandeira do cartão na tabela de taxas (PAG-01 CA 2). <c>Any</c> vale para qualquer bandeira sem taxa própria.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CardBrand>))]
public enum CardBrand
{
    Any,
    Visa,
    Mastercard,
    Elo,
    Amex,
    Hipercard,
    Diners,
    Cabal,
    Other,
}

/// <summary>
/// Taxa e prazo vigentes numa data (PAG-01 CA 5): bandeira, a partir de quantas parcelas, taxa em % (<c>numeric(9,4)</c>), prazo em dias
/// (D+N) e o início da vigência.
/// </summary>
public sealed record AcquirerFeeTerms(CardBrand Brand, int Installments, decimal FeePercent, int SettlementDays, DateOnly ValidFrom);

/// <summary>
/// Forma de pagamento ativa para os outros módulos (PAG-01 CA 5). <c>AllowsChange</c> só no Dinheiro; <c>NfePaymentCode</c> é o
/// <c>tPag</c> da NFC-e; <c>Confirmed</c> falso enquanto os valores do seed não foram conferidos; <c>Fees</c> são as taxas vigentes na data
/// pedida (vazio = sem taxa, o recebível sai sem desconto).
/// </summary>
public sealed record PaymentMethodInfo(
    Guid Id,
    string Name,
    PaymentMethodType Type,
    bool AllowsChange,
    Guid DestinationAccountId,
    Guid? AcquirerPartyId,
    string NfePaymentCode,
    bool Confirmed,
    IReadOnlyList<AcquirerFeeTerms> Fees);

/// <summary>
/// Formas de pagamento do Financeiro para o PDV, o núcleo de vendas e o fechamento de caixa (PAG-01, rascunho C §8): o Financeiro é o dono;
/// os outros módulos só leem por aqui. Nenhum campo guarda dado de cartão de cliente.
/// </summary>
public interface IPaymentMethods
{
    /// <summary>As formas ativas, com as taxas vigentes em <paramref name="on"/>.</summary>
    Task<IReadOnlyList<PaymentMethodInfo>> ActiveAsync(DateOnly on, CancellationToken cancellationToken = default);

    /// <summary>
    /// Taxa e prazo de uma venda: a bandeira exata ou, sem taxa própria, a de "qualquer bandeira"; das parcelas, a faixa com o maior
    /// "a partir de" que não passa de <paramref name="installments"/>. Null se a forma não tem taxa vigente para o caso.
    /// </summary>
    Task<AcquirerFeeTerms?> FeeAsync(Guid paymentMethodId, CardBrand brand, int installments, DateOnly on, CancellationToken cancellationToken = default);
}

/// <summary>Quem usa uma forma de pagamento (PAG-01 CA 6): a venda (VND-01) implementa. Em uso, a forma não é excluída, só desativada.</summary>
public interface IPaymentMethodUsageProvider
{
    /// <summary>Descrição curta do uso ("vendas") ou null se não usa.</summary>
    Task<string?> UsageAsync(Guid paymentMethodId, CancellationToken cancellationToken = default);
}

/// <summary>Forma de pagamento criada, alterada, conferida, desativada ou com taxa nova: o que desce para os caixas muda (PAG-01).</summary>
public sealed record PaymentMethodChanged(Guid PaymentMethodId) : IIntegrationEvent;
