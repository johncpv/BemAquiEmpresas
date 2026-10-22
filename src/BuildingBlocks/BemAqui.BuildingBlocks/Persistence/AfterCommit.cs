using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BemAqui.BuildingBlocks.Persistence;

/// <summary>
/// Avisos que só saem depois do commit (ADR 0006, ADR 0023 §7): tempo real (SignalR) e fechamento de conexões. Chame no fim do
/// comando: com transação aberta no DbContext do escopo (comando com transação ou consumidor do outbox), roda no commit dela e
/// some no rollback; sem transação e com mudanças pendentes, roda depois do próximo SaveChanges que der certo; sem nada
/// pendente, roda já. A falha do aviso só vai para o log: o dado já está gravado.
/// </summary>
public interface IAfterCommit
{
    Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fila do escopo, esvaziada pelo <see cref="AfterCommitInterceptor"/>. Cada aviso lembra a transação em que nasceu: só sai no
/// commit dela; o de uma transação desfeita (rollback ou descartada sem commit) é jogado fora.
/// </summary>
public sealed partial class AfterCommitActions(ILogger<AfterCommitActions>? logger = null)
{
    private readonly ILogger _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    private readonly List<(Guid? TransactionId, Func<CancellationToken, Task> Action)> _pending = [];

    public int Count => _pending.Count;

    public void Add(Guid? transactionId, Func<CancellationToken, Task> action) => _pending.Add((transactionId, action));

    /// <summary>Rollback ou falha: descarta os avisos da transação (ou os sem transação, na falha do SaveChanges).</summary>
    public void Discard(Guid? transactionId) => _pending.RemoveAll(p => p.TransactionId == transactionId);

    /// <summary>
    /// Commit de <paramref name="transactionId"/> (ou SaveChanges sem transação, com null): roda os avisos dela e os sem transação;
    /// os de outras transações, que já não existem, são descartados.
    /// </summary>
    public async Task FlushAsync(Guid? transactionId, CancellationToken cancellationToken = default)
    {
        while (_pending.Count > 0)
        {
            var batch = _pending.ToList();
            _pending.Clear();
            foreach (var (owner, action) in batch)
            {
                if (owner is null || owner == transactionId)
                {
                    await RunSafeAsync(action, cancellationToken);
                }
            }
        }
    }

    internal async Task RunSafeAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aviso depois do commit falhou; o dado já está gravado.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}

public sealed class AfterCommit(AfterCommitActions queue, BemAquiDbContext db) : IAfterCommit
{
    public Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (db.Database.CurrentTransaction is { } transaction)
        {
            queue.Add(transaction.TransactionId, action);
            return Task.CompletedTask;
        }

        if (db.ChangeTracker.HasChanges())
        {
            queue.Add(null, action);
            return Task.CompletedTask;
        }

        return queue.RunSafeAsync(action, cancellationToken);
    }
}

/// <summary>
/// Esvazia a <see cref="AfterCommitActions"/> no commit da transação (explícita ou a interna do SaveChanges) e depois do
/// SaveChanges sem transação; descarta no rollback e na falha.
/// </summary>
public sealed class AfterCommitInterceptor(AfterCommitActions queue) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (NoTransaction(eventData))
        {
            queue.FlushAsync(null).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (NoTransaction(eventData))
        {
            await queue.FlushAsync(null, cancellationToken);
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (NoTransaction(eventData))
        {
            queue.Discard(null);
        }
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        queue.FlushAsync(eventData?.TransactionId).GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        queue.FlushAsync(eventData?.TransactionId, cancellationToken);

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => queue.Discard(eventData?.TransactionId);

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        queue.Discard(eventData?.TransactionId);
        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => queue.Discard(eventData?.TransactionId);

    public Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        queue.Discard(eventData?.TransactionId);
        return Task.CompletedTask;
    }

    private static bool NoTransaction(DbContextEventData eventData) => eventData.Context?.Database.CurrentTransaction is null;
}
