using BemAqui.Finance.Contracts;

namespace BemAqui.Finance.Application;

/// <summary>Criar ou alterar uma forma de pagamento (PAG-01 CA 1 e 7). <c>NfePaymentCode</c> vazio usa o mais provável para o tipo.</summary>
public sealed record PaymentMethodRequest(string Name, PaymentMethodType Type, Guid DestinationAccountId, Guid? AcquirerPartyId, string? NfePaymentCode);

/// <summary>Nova taxa (PAG-01 CA 2 e 3): bandeira, a partir de quantas parcelas, taxa em %, prazo em dias (D+N) e o início da vigência.</summary>
public sealed record AcquirerFeeInput(CardBrand Brand, int Installments, decimal FeePercent, int SettlementDays, DateOnly ValidFrom);

/// <summary>Uma linha da tabela de taxas, vigente ou encerrada, com quem cadastrou e quando (o histórico do CA 3).</summary>
public sealed record AcquirerFeeView(
    Guid Id, CardBrand Brand, int Installments, decimal FeePercent, int SettlementDays, DateOnly ValidFrom, DateOnly? ValidTo, bool InForce,
    DateTimeOffset CreatedAt, Guid? CreatedBy, string? CreatedByName);

/// <summary>Forma na lista e no detalhe: a conta e a adquirente com nome, o selo de conferência e as taxas vigentes hoje.</summary>
public sealed record PaymentMethodDetail(
    Guid Id,
    string Name,
    PaymentMethodType Type,
    Guid DestinationAccountId,
    string? DestinationAccountName,
    Guid? AcquirerPartyId,
    string? AcquirerName,
    string NfePaymentCode,
    bool AllowsChange,
    bool IsActive,
    string? SeedKey,
    bool Confirmed,
    DateTimeOffset? ConfirmedAt,
    string? ConfirmedByName,
    IReadOnlyList<AcquirerFeeView> CurrentFees,
    DateTimeOffset CreatedAt);

/// <summary>Adquirente para o seletor da forma (terceiro ativo com o papel Adquirente).</summary>
public sealed record AcquirerChoice(Guid Id, string Name, string? Document);
