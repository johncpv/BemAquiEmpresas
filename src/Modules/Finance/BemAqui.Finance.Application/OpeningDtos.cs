using System.Text.Json.Serialization;

namespace BemAqui.Finance.Application;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CutoverInput(DateOnly CutoverDate);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningBalanceLine(Guid BankAccountId, decimal? Balance);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningBalancesInput(IReadOnlyList<OpeningBalanceLine> Lines);

/// <summary>Título a receber em aberto na data de corte (CA 5): o saldo em aberto vira o valor.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningReceivableLine(Guid PartyId, string Description, string? Reference, DateOnly? IssueDate, DateOnly DueDate, decimal Balance, Guid? AccountId, Guid? CostCenterId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningReceivablesInput(IReadOnlyList<OpeningReceivableLine> Lines);

/// <summary>Título a pagar em aberto (CA 6): o saldo já é o líquido, sem retenções.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningPayableLine(Guid PartyId, string Description, string? Reference, DateOnly DueDate, decimal Balance, Guid? AccountId, Guid? CostCenterId, PaymentDetailsInput? PaymentDetails);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OpeningPayablesInput(IReadOnlyList<OpeningPayableLine> Lines);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CompleteOpeningInput(string? Notes, uint Version);

public sealed record OpeningAccount(Guid Id, string Name, decimal? OpeningBalance, DateOnly? OpeningDate, bool HasMovement, decimal SystemBalanceAtCutover);

public sealed record OpeningPartyTotal(Guid PartyId, string PartyName, int Count, decimal Total);

/// <summary>Situação da implantação e a conferência (CA 9): saldos por conta e os títulos de implantação por terceiro.</summary>
public sealed record OpeningStatus(
    DateOnly? CutoverDate,
    DateOnly SuggestedCutover,
    DateTimeOffset? CompletedAt,
    string? Notes,
    bool HasMovement,
    IReadOnlyList<OpeningAccount> Accounts,
    IReadOnlyList<OpeningPartyTotal> Receivables,
    IReadOnlyList<OpeningPartyTotal> Payables,
    uint Version);

public sealed record OpeningBatchResult(int Count, decimal Total);
