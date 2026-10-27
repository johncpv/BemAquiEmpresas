using BemAqui.Finance.Application.Inter;
using BemAqui.Finance.Domain;

namespace BemAqui.Finance.Application.Statements;

/// <summary>Lançamento devolvido pela API do banco, já com o sinal (crédito positivo, débito negativo).</summary>
public sealed record ProviderStatementLine(string ExternalId, DateOnly Date, decimal Amount, string Description, StatementLineKind Kind, string? CounterpartyName, string? CounterpartyDocument)
{
    public override string ToString() => $"ProviderStatementLine({Date:yyyy-MM-dd})";
}

/// <summary>Extrato de um período pela API: os lançamentos e quantos vieram fora do formato (sem id, data ou valor).</summary>
public sealed record ProviderStatement(IReadOnlyList<ProviderStatementLine> Lines, int Rejected);

/// <summary>
/// Extrato pela API do banco (ADR 0032, <c>02-arquitetura</c> §6.2): o Finance só conhece este contrato; o
/// <c>InterStatementProvider</c> (Finance.Infrastructure) fala com o Inter pelo <see cref="IInterApiClient"/>. Outro banco, outra
/// implementação.
/// </summary>
public interface IBankStatementProvider
{
    BankProvider Provider { get; }

    /// <summary>Lançamentos de <paramref name="from"/> a <paramref name="to"/> (inclusive), todas as páginas.</summary>
    Task<InterResult<ProviderStatement>> GetAsync(InterCredentials credentials, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Saldo do banco no fim do dia (<paramref name="date"/>).</summary>
    Task<InterResult<decimal>> GetBalanceAsync(InterCredentials credentials, DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>Linha do extrato do banco na tela (CA 8): contraparte inteira só com <c>finance.edit</c>.</summary>
public sealed record BankStatementLineView(
    Guid Id, DateOnly Date, string Description, string Kind, string? CounterpartyName, string? CounterpartyDocument, bool CounterpartyMasked, decimal Amount, string Source,
    bool BankVerified, string Status, string? IgnoreReason, Guid BatchId, uint Version);

/// <summary>Saldo do banco num dia, com o do sistema e a diferença (CA 3).</summary>
public sealed record BankBalanceDay(DateOnly Date, decimal BankBalance, decimal SystemBalance, decimal Difference, string Source);

/// <summary>Contingência do OFX liberada (CA 6): até quando, por quem e o motivo.</summary>
public sealed record OfxContingencyView(DateOnly ValidUntil, string? GrantedByName, DateTimeOffset GrantedAt, string Reason);

/// <summary>Lote na tela e no histórico (CA 12).</summary>
public sealed record BankStatementBatchView(
    Guid Id, string Source, string Trigger, DateOnly? PeriodFrom, DateOnly? PeriodTo, string? FileName, string? FileSha256, Guid? AttachmentId, bool BankVerified,
    int NewCount, int DuplicateCount, int RejectedCount, string? Notes, DateTimeOffset ImportedAt, string? ImportedByName, DateTimeOffset? UndoneAt, string? UndoneByName,
    string? UndoReason, int ReconciledCount, uint Version);

/// <summary>
/// Aba "Extrato do banco" da conta (CA 8): as linhas filtradas, o saldo do banco por dia, a faixa do saldo mais recente, o que a conta
/// aceita (API, OFX, contingência) e os lotes recentes.
/// </summary>
public sealed record BankStatementView(
    Guid AccountId, string AccountName, string Provider, bool ApiConnected, string? Environment, bool OfxAllowed, OfxContingencyView? Contingency, DateOnly From, DateOnly To,
    BankBalanceDay? Latest, IReadOnlyList<BankBalanceDay> Balances, IReadOnlyList<BankStatementLineView> Lines, int Total, int PendingCount, bool CounterpartyMasked,
    IReadOnlyList<BankStatementBatchView> Batches, DateTimeOffset? LastFetchAt, DateTimeOffset? NextFetchAt);

/// <summary>Resumo de uma importação (CA 5): "{n} novas, {m} já importadas, {k} recusadas" e os motivos.</summary>
public sealed record BankStatementImportResult(
    Guid BatchId, int NewCount, int DuplicateCount, int RejectedCount, IReadOnlyList<string> Notes, DateOnly? PeriodFrom, DateOnly? PeriodTo, int PendingCount, bool BankVerified);

public sealed record IgnoreStatementLineInput(string Reason, uint Version);

public sealed record UndoStatementBatchInput(string Reason, uint Version);

public sealed record OfxContingencyInput(string Reason, DateOnly ValidUntil);
