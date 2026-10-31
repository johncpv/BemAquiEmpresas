using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Reconciliation;

public sealed partial class ReconciliationService
{
    public const string NotReadyCode = "reconciliation_not_ready";

    /// <summary>
    /// "Conciliada até dd/mm/aaaa" (CA 11): o dia mais recente, depois do último fechamento, em que todas as linhas até ele estão conciliadas
    /// ou ignoradas e o saldo do banco no dia bate com o do sistema. Sem dia novo, vale o último fechamento.
    /// </summary>
    public async Task<DateOnly?> ReconciledUntilAsync(Guid accountId, DateOnly? closedUntil, CancellationToken cancellationToken = default)
    {
        var firstPending = await db.BankStatementLines.AsNoTracking().Where(l => l.BankAccountId == accountId && l.Status == StatementLineStatus.Pending)
            .MinAsync(l => (DateOnly?)l.Date, cancellationToken);
        var after = closedUntil ?? DateOnly.MinValue;
        var candidates = await BankBalancesAsync(accountId, after, firstPending?.AddDays(-1) ?? Today, 10, cancellationToken);
        foreach (var (date, bank) in candidates)
        {
            if (bank == await balances.BankBalanceAsync([accountId], date, cancellationToken))
            {
                return date;
            }
        }

        return closedUntil;
    }

    /// <summary>Prévia do fechamento para o <c>ConfirmDialog</c> com o impacto em lista (CA 11): linhas, totais, saldos e a diferença.</summary>
    public async Task<Result<ClosingPreview>> ClosingPreviewAsync(Guid accountId, DateOnly until, CancellationToken cancellationToken = default)
    {
        if (!await db.BankAccounts.AnyAsync(a => a.Id == accountId, cancellationToken))
        {
            return NotFound();
        }

        var closedUntil = await period.ClosedUntilAsync(accountId, cancellationToken);
        var from = closedUntil?.AddDays(1);
        var lines = await db.BankStatementLines.AsNoTracking()
            .Where(l => l.BankAccountId == accountId && l.Status != StatementLineStatus.Undone && l.Date <= until && (from == null || l.Date >= from))
            .Select(l => new { l.Status, l.Amount }).ToListAsync(cancellationToken);
        var pendingBefore = await db.BankStatementLines.AsNoTracking().CountAsync(l => l.BankAccountId == accountId && l.Status == StatementLineStatus.Pending && l.Date <= until, cancellationToken);
        var bank = (await BankBalancesAsync(accountId, until.AddDays(-1), until, 1, cancellationToken)).Select(b => (decimal?)b.Balance).FirstOrDefault();
        var system = await balances.BankBalanceAsync([accountId], until, cancellationToken);
        var counted = lines.Where(l => l.Status != StatementLineStatus.Ignored).ToList();
        string? refusal = null;
        if (closedUntil is { } closed && until <= closed)
        {
            refusal = $"A conciliação já está fechada até {closed:dd/MM/yyyy}.";
        }
        else if (until > Today)
        {
            refusal = "Escolha uma data até hoje.";
        }
        else if (pendingBefore > 0)
        {
            refusal = $"{pendingBefore} {(pendingBefore == 1 ? "linha pendente" : "linhas pendentes")} até {until:dd/MM/yyyy}: concilie ou ignore antes de fechar.";
        }
        else if (bank is null)
        {
            refusal = $"Sem o saldo do banco em {until:dd/MM/yyyy}: importe o extrato até esse dia.";
        }
        else if (bank != system)
        {
            refusal = $"O saldo do banco em {until:dd/MM/yyyy} ({ReceivableService.Money(bank.Value)}) não bate com o do sistema ({ReceivableService.Money(system)}).";
        }

        return new ClosingPreview(until, from, refusal is null, refusal, lines.Count, lines.Count(l => l.Status == StatementLineStatus.Reconciled), lines.Count(l => l.Status == StatementLineStatus.Ignored),
            counted.Where(l => l.Amount > 0).Sum(l => l.Amount), counted.Where(l => l.Amount < 0).Sum(l => l.Amount), bank, system, bank - system);
    }

    /// <summary>"Fechar conciliação até dd/mm" (CA 11): com <c>finance.approve</c> (no endpoint), só com tudo conciliado e diferença zero.</summary>
    public async Task<Result<ReconciliationClosingView>> CloseAsync(Guid accountId, CloseReconciliationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var preview = await ClosingPreviewAsync(accountId, input.Until, cancellationToken);
        if (preview.IsFailure)
        {
            return preview.Error!;
        }

        if (!preview.Value.Allowed)
        {
            return Error.Conflict(NotReadyCode, preview.Value.Refusal!);
        }

        var closing = new BankReconciliationClosing
        {
            BankAccountId = accountId, UntilDate = input.Until, LineCount = preview.Value.Lines, BankBalance = preview.Value.BankBalance!.Value, SystemBalance = preview.Value.SystemBalance,
            ClosedAt = clock.GetUtcNow(), ClosedBy = currentUser.UserId,
        };
        db.BankReconciliationClosings.Add(closing);
        var account = await db.BankAccounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.Name).FirstAsync(cancellationToken);
        var who = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, cancellationToken) ?? "Um usuário" : "O sistema";
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, NotificationType, $"{who} fechou a conciliação da conta {account} até {input.Until:dd/MM/yyyy}.", Link(accountId));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new ReconciliationClosingView(closing.Id, closing.UntilDate, closing.LineCount, closing.BankBalance, closing.SystemBalance, who, closing.ClosedAt);
    }

    /// <summary>Saldos do banco por dia (a API antes do OFX no mesmo dia), do mais recente para trás, entre as datas (exclusive o início).</summary>
    private async Task<List<(DateOnly Date, decimal Balance)>> BankBalancesAsync(Guid accountId, DateOnly after, DateOnly until, int take, CancellationToken ct)
    {
        var rows = await db.BankStatementBalances.AsNoTracking().Where(b => b.BankAccountId == accountId && b.Date > after && b.Date <= until)
            .OrderByDescending(b => b.Date).Take(take * 2).Select(b => new { b.Date, b.Balance, b.Source }).ToListAsync(ct);
        return [.. rows.GroupBy(r => r.Date).Select(g => (g.Key, g.OrderBy(r => r.Source == StatementSource.Api ? 0 : 1).First().Balance)).Take(take)];
    }
}
