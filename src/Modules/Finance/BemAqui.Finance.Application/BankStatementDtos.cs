using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application;

/// <summary>Linha do extrato (FIN-04 CA 1): valor com sinal, saldo acumulado e o documento de origem com link.</summary>
public sealed record StatementLine(
    Guid TransactionId, DateOnly Date, string Description, string Type, string? Origin, string? Link, decimal Amount, decimal Balance,
    bool WithoutOrder, Guid? TransferId, Guid? BankEntryId, bool IsReversal, bool Reversed, uint? MovementVersion);

public sealed record StatementPage(IReadOnlyList<StatementLine> Items, int Page, int PageSize, int Total);

/// <summary>
/// Extrato de um período: a linha de partida ("Saldo inicial" quando o período começa na data do saldo inicial, senão
/// "Saldo anterior"), as linhas paginadas e o saldo final do período.
/// </summary>
public sealed record StatementResult(
    Guid AccountId, string AccountName, DateOnly From, DateOnly To, bool OpeningMissing, bool HasMovement, string StartLabel, decimal StartBalance, decimal EndBalance, StatementPage Lines);

public sealed record AccountBalance(Guid Id, string Name, decimal CurrentBalance, bool OpeningMissing);

public sealed record AccountBalances(IReadOnlyList<AccountBalance> Accounts, decimal Consolidated);

public sealed record TransferInput(Guid FromAccountId, Guid ToAccountId, decimal Amount, DateOnly Date, string? Description, string? BackdateReason);

public sealed record BankTransferDetail(Guid Id, Guid FromAccountId, Guid ToAccountId, decimal Amount, DateOnly Date, string Description, bool Reversed, string? ReversalReason, uint Version);

public sealed record BankEntryInput(Guid BankAccountId, BankEntryKind Kind, DateOnly Date, decimal Amount, Guid? AccountId, Guid? CostCenterId, string? Reason, string? BackdateReason);

public sealed record BankEntryDetail(Guid Id, Guid BankAccountId, BankEntryKind Kind, DateOnly Date, decimal Amount, Guid AccountId, string Reason, bool Reversed, string? ReversalReason, uint Version);
