using BemAqui.BuildingBlocks.Authorization;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Statements;

public sealed partial class BankStatementImportService
{
    public const string DebitNeedsApprove = "Ignorar uma linha de saída exige a aprovação em Caixa e pagamentos.";

    /// <summary>
    /// "Ignorar linha" (CA 9): a linha nunca é editada nem apagada; uma linha errada fica "Ignorada" com motivo. Crédito com
    /// <c>finance.edit</c> (no endpoint); débito também com <c>finance.approve</c> (§4.3). Linha de outra conta responde 404 (IDOR).
    /// </summary>
    public async Task<Result<BankStatementLineView>> IgnoreAsync(Guid accountId, Guid lineId, IgnoreStatementLineInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var line = await db.BankStatementLines.FirstOrDefaultAsync(l => l.Id == lineId && l.BankAccountId == accountId && l.Status != StatementLineStatus.Undone, cancellationToken);
        if (line is null)
        {
            return Error.NotFound(Error.Codes.NotFound, LineNotFoundMessage);
        }

        db.Entry(line).Property(l => l.Version).OriginalValue = input.Version;
        if (line.Status != StatementLineStatus.Pending)
        {
            return Error.Conflict("bank_statement_line_not_pending", "Só uma linha pendente pode ser ignorada.");
        }

        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 200)
        {
            return Invalid("reason", "Informe o motivo (até 200 caracteres).");
        }

        if (line.IsDebit && !await permissions.HasPermissionAsync("finance", StandardActions.Approve, cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, DebitNeedsApprove);
        }

        line.Ignore(reason, currentUser.UserId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return View(line, full: true);
    }

    /// <summary>
    /// "Desfazer importação" (CA 9): com motivo, enquanto nenhuma linha do lote estiver conciliada. As linhas viram <c>Undone</c> (saem do
    /// extrato e do índice único, então o mesmo arquivo pode entrar de novo) e o saldo trazido pelo lote sai; o lote guarda quem, quando e o
    /// motivo (auditado). O Proprietário é avisado.
    /// </summary>
    public async Task<Result<BankStatementBatchView>> UndoAsync(Guid accountId, Guid batchId, UndoStatementBatchInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var batch = await db.BankStatementBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.BankAccountId == accountId, cancellationToken);
        if (batch is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Importação não encontrada.");
        }

        db.Entry(batch).Property(b => b.Version).OriginalValue = input.Version;
        if (batch.IsUndone)
        {
            return Error.Conflict("bank_statement_batch_undone", "Esta importação já foi desfeita.");
        }

        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 200)
        {
            return Invalid("reason", "Informe o motivo (até 200 caracteres).");
        }

        var account = await AccountAsync(accountId, cancellationToken);
        await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            await LockAccountAsync(accountId, cancellationToken);
            var reconciled = await db.BankStatementLines.CountAsync(l => l.BatchId == batchId && l.Status == StatementLineStatus.Reconciled, cancellationToken);
            if (reconciled > 0)
            {
                return Error.Conflict("bank_statement_batch_reconciled", $"{reconciled} {(reconciled == 1 ? "linha deste lote já está conciliada" : "linhas deste lote já estão conciliadas")}. Desfaça a conciliação antes.");
            }

            var lines = await db.BankStatementLines.CountAsync(l => l.BatchId == batchId && l.Status != StatementLineStatus.Undone, cancellationToken);
            batch.Undo(currentUser.UserId, clock.GetUtcNow(), reason);
            await NotifyOwnersAsync($"{await WhoAsync(cancellationToken)} desfez a importação do extrato da conta {account?.Name} ({lines} {(lines == 1 ? "linha" : "linhas")}). Motivo: {reason}",
                BatchLink(accountId, batchId), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await db.BankStatementLines.Where(l => l.BatchId == batchId && l.Status != StatementLineStatus.Reconciled)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Status, StatementLineStatus.Undone), cancellationToken);
            await db.BankStatementBalances.Where(b => b.BatchId == batchId && b.Source == batch.Source).ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return (await BatchViewsAsync(accountId, 200, cancellationToken)).First(b => b.Id == batchId);
    }
}
