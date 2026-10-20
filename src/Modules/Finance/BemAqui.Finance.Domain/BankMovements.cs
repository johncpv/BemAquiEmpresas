using System.Text.Json.Serialization;
using BemAqui.SharedKernel;

namespace BemAqui.Finance.Domain;

/// <summary>
/// Transferência entre contas (<c>finance.bank_transfers</c>, FIN-04 CA 5): duas transações <c>Transfer</c> ligadas por
/// <c>transfer_id</c>; o estorno cria as inversas e marca a transferência. Nada é apagado.
/// </summary>
[DocumentName("A transferência")]
public sealed class BankTransfer : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid FromAccountId { get; init; }

    public Guid ToAccountId { get; init; }

    public decimal Amount { get; init; }

    public DateOnly Date { get; init; }

    public string Description { get; init; } = string.Empty;

    public string? BackdateReason { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public string? ReversalReason { get; set; }

    public uint Version { get; set; }
}

/// <summary>Tipo do lançamento sem OP (D4): o sinal sai do tipo (saída: tarifa, IOF e juros pagos).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BankEntryKind>))]
public enum BankEntryKind
{
    BankFee,
    Iof,
    InterestPaid,
    InterestReceived,
    Yield,
    OtherOut,
    OtherIn,
}

/// <summary>
/// Lançamento bancário sem OP (<c>finance.bank_entries</c>, FIN-04 CA 11): tarifa, IOF, juros e rendimento, com conta do
/// plano e motivo; uma transação <c>BankEntry</c>; o Proprietário é avisado de todo lançamento e de todo estorno.
/// </summary>
[DocumentName("O lançamento")]
public sealed class BankEntry : Entity, IAuditable, IConcurrencyVersioned
{
    public Guid BankAccountId { get; init; }

    public BankEntryKind Kind { get; init; }

    public DateOnly Date { get; init; }

    /// <summary>Com sinal: positivo entra, negativo sai.</summary>
    public decimal Amount { get; init; }

    public Guid AccountId { get; init; }

    public Guid? CostCenterId { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string? BackdateReason { get; init; }

    public Guid? TransactionId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public Guid? CreatedBy { get; init; }

    public DateTimeOffset? ReversedAt { get; set; }

    public Guid? ReversedBy { get; set; }

    public string? ReversalReason { get; set; }

    public uint Version { get; set; }

    public static bool IsOutflow(BankEntryKind kind) => kind is BankEntryKind.BankFee or BankEntryKind.Iof or BankEntryKind.InterestPaid or BankEntryKind.OtherOut;

    /// <summary>Conta do plano sugerida pelo tipo (CA 11).</summary>
    public static string SuggestedAccountCode(BankEntryKind kind) => IsOutflow(kind) ? "3.5" : "1.3";
}
