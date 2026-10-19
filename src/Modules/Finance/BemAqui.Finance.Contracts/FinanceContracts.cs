using System.Text.Json.Serialization;

namespace BemAqui.Finance.Contracts;

/// <summary>Tipo de conta (CAD-05b CA 2): só Banco e Caixa.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BankAccountKind>))]
public enum BankAccountKind
{
    Bank,
    Cash,
}

/// <summary>Parcela de uma condição: dias a partir da data base e percentual (<c>numeric(7,4)</c>, soma 100).</summary>
public sealed record Installment(int Days, decimal Percent);

/// <summary>Parcela calculada para um valor e uma data base (CA 13).</summary>
public sealed record InstallmentDue(int Number, DateOnly DueDate, decimal Amount);

public sealed record PaymentTermSummary(Guid Id, string Name, bool IsActive, IReadOnlyList<Installment> Installments);

/// <summary>
/// Condições de pagamento para os outros módulos (ADR 0016): Parties valida a condição padrão; CR, CP e pedido de compra
/// (Fase 2) geram as parcelas com <see cref="SplitAsync"/>, a mesma divisão da prévia da tela.
/// </summary>
public interface IPaymentTerms
{
    /// <summary>Por id, inclusive inativa (documento antigo continua mostrando o nome); null se não existe ou foi excluída.</summary>
    Task<PaymentTermSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Só as ativas, para os seletores (CA 16 e 17).</summary>
    Task<IReadOnlyList<PaymentTermSummary>> ActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Parcelas de <paramref name="amount"/> a partir de <paramref name="baseDate"/>; null se a condição não existe.</summary>
    Task<IReadOnlyList<InstallmentDue>?> SplitAsync(Guid termId, decimal amount, DateOnly baseDate, CancellationToken cancellationToken = default);
}

/// <summary>Quem usa uma condição (CA 17): cada módulo que guarda o id implementa. Em uso, a condição não é excluída, só inativada.</summary>
public interface IPaymentTermUsageProvider
{
    /// <summary>Descrição curta do uso ("terceiros") ou null se não usa.</summary>
    Task<string?> UsageAsync(Guid termId, CancellationToken cancellationToken = default);
}
