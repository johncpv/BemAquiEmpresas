using System.Diagnostics;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.Finance.Application.Statements;

/// <summary>
/// Extrato do banco dentro do sistema (BNK-03a, R-FIN-8): linhas importadas pela API do Inter (job diário e "Buscar agora") e por
/// arquivo OFX, sem duplicar (CA 2 e 5), saldo do banco conferido com o do sistema (CA 3), consulta com a contraparte mascarada para
/// quem só vê (CA 8), linhas imutáveis com "Ignorar" e "Desfazer importação" (CA 9). As gravações de uma conta passam por um advisory
/// lock da conta (job e "Buscar agora" ao mesmo tempo esperam um pelo outro). O log traz só conta, período, contagens e duração (CA 11).
/// </summary>
public sealed partial class BankStatementImportService(
    IFinanceDb db,
    BankIntegrationService integrations,
    IEnumerable<IBankStatementProvider> providers,
    AttachmentService attachments,
    CashFlowService balances,
    IPermissionChecker permissions,
    ICurrentUser currentUser,
    IUserDirectory users,
    INotificationService notifications,
    IOptions<BankStatementOptions> options,
    Reconciliation.ReconciliationPeriodGuard period,
    TimeProvider clock,
    ILogger<BankStatementImportService> logger)
{
    public const string AttachmentOwner = "BankStatementBatch";
    public const string FileIndex = "ux_bank_statement_batches_account_file";
    public const string ExternalIndex = "ux_bank_statement_lines_account_source_external";
    public const string ImportNotification = "finance.bank_statement_import";
    public const string DifferenceNotification = "finance.bank_statement_difference";
    public const string ContingencyNotification = "finance.bank_ofx_contingency";
    public const string NotFoundMessage = "Conta bancária não encontrada.";
    public const string LineNotFoundMessage = "Linha do extrato não encontrada.";
    public const string Tab = "extrato-banco";

    private BankStatementOptions Settings => options.Value;

    /// <summary>Aba "Extrato do banco" da conta (link dos avisos e do próximo passo).</summary>
    public static string Link(Guid bankAccountId) => $"/app/financeiro/contas/{bankAccountId}/extrato?aba={Tab}";

    public static string BatchLink(Guid bankAccountId, Guid batchId) => Link(bankAccountId) + "&lote=" + batchId;

    /// <summary>
    /// CA 8: linhas do período (padrão: o mês corrente) e da situação, paginadas, com o saldo do banco por dia, a faixa do saldo mais
    /// recente e os lotes. A contraparte sai inteira só com <c>finance.edit</c>.
    /// </summary>
    public async Task<Result<BankStatementView>> ViewAsync(Guid accountId, DateOnly? from, DateOnly? to, string? status, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var account = await AccountAsync(accountId, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        var today = BusinessTime.Today(clock);
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? start.AddMonths(1).AddDays(-1);
        if (end < start || end.DayNumber - start.DayNumber > 400)
        {
            return Error.Validation(Error.Codes.Validation, "Escolha um período de até 400 dias, com o fim depois do início.", new Dictionary<string, string[]> { ["to"] = ["Escolha um período de até 400 dias, com o fim depois do início."] });
        }

        StatementLineStatus? filter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<StatementLineStatus>(status, ignoreCase: true, out var parsed) || parsed == StatementLineStatus.Undone)
            {
                return Error.Validation(Error.Codes.Validation, "Situação inválida. Use Pending, Reconciled ou Ignored.");
            }

            filter = parsed;
        }

        var lines = db.BankStatementLines.AsNoTracking().Where(l => l.BankAccountId == accountId && l.Status != StatementLineStatus.Undone && l.Date >= start && l.Date <= end);
        if (filter is { } f)
        {
            lines = lines.Where(l => l.Status == f);
        }

        var total = await lines.CountAsync(cancellationToken);
        var rows = await lines.OrderBy(l => l.Date).ThenBy(l => l.ImportedAt).ThenBy(l => l.Sequence).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        var full = await permissions.HasPermissionAsync("finance", StandardActions.Edit, cancellationToken);
        var pending = await db.BankStatementLines.CountAsync(l => l.BankAccountId == accountId && l.Status == StatementLineStatus.Pending, cancellationToken);
        var integration = await db.BankIntegrations.AsNoTracking().FirstOrDefaultAsync(i => i.BankAccountId == accountId, cancellationToken);
        var api = account.Provider != BankProvider.None && integration is { IsActive: true };
        var contingency = await ContingencyAsync(accountId, today, cancellationToken);
        var balanceDays = await BalanceDaysAsync(account, start, end, cancellationToken);
        var latest = await LatestBalanceAsync(account, cancellationToken);
        var batches = await BatchViewsAsync(accountId, 10, cancellationToken);
        var lastFetch = await db.BankStatementBatches.AsNoTracking().Where(b => b.BankAccountId == accountId && b.Source == StatementSource.Api)
            .OrderByDescending(b => b.ImportedAt).Select(b => (DateTimeOffset?)b.ImportedAt).FirstOrDefaultAsync(cancellationToken);
        var nextFetch = lastFetch is { } last && last + Settings.FetchCooldown > clock.GetUtcNow() ? last + Settings.FetchCooldown : (DateTimeOffset?)null;
        return new BankStatementView(account.Id, account.Name, account.Provider.ToString(), api, api ? integration!.Environment.ToString() : null, !api || contingency is not null,
            contingency, start, end, latest, balanceDays, rows.Select(l => View(l, full)).ToList(), total, pending, !full, batches, lastFetch, nextFetch);
    }

    /// <summary>Histórico das importações da conta (CA 12): lotes com quem, quando, origem, hash, contagens e motivo.</summary>
    public async Task<Result<IReadOnlyList<BankStatementBatchView>>> BatchesAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (await AccountAsync(accountId, cancellationToken) is null)
        {
            return NotFound();
        }

        return Result<IReadOnlyList<BankStatementBatchView>>.Success(await BatchViewsAsync(accountId, 200, cancellationToken));
    }

    private static BankStatementLineView View(BankStatementLine l, bool full) => new(
        l.Id, l.Date, l.Description, l.Kind.ToString(),
        full ? l.CounterpartyName : CounterpartyMask.Name(l.CounterpartyName),
        full ? (l.CounterpartyDocument is { } d ? BrazilianDocuments.Format(d) : null) : CounterpartyMask.Document(l.CounterpartyDocument),
        !full && (l.CounterpartyName is not null || l.CounterpartyDocument is not null),
        l.Amount, l.Source.ToString(), l.BankVerified, l.Status.ToString(), l.IgnoreReason, l.BatchId, l.Version);

    private async Task<IReadOnlyList<BankStatementBatchView>> BatchViewsAsync(Guid accountId, int take, CancellationToken ct)
    {
        var batches = await db.BankStatementBatches.AsNoTracking().Where(b => b.BankAccountId == accountId).OrderByDescending(b => b.ImportedAt).Take(take).ToListAsync(ct);
        var ids = batches.Select(b => b.Id).ToList();
        var reconciled = await db.BankStatementLines.AsNoTracking().Where(l => ids.Contains(l.BatchId) && l.Status == StatementLineStatus.Reconciled)
            .GroupBy(l => l.BatchId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);
        var names = new Dictionary<Guid, string?>();
        foreach (var user in batches.SelectMany(b => new[] { b.ImportedBy, b.UndoneBy }).Where(u => u is not null).Select(u => u!.Value).Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, ct);
        }

        return batches.Select(b => new BankStatementBatchView(b.Id, b.Source.ToString(), b.Trigger.ToString(), b.PeriodFrom, b.PeriodTo, b.FileName, b.FileSha256, b.AttachmentId, b.BankVerified,
            b.NewCount, b.DuplicateCount, b.RejectedCount, b.Notes, b.ImportedAt, b.ImportedBy is { } by ? names.GetValueOrDefault(by) ?? "um usuário" : "o sistema",
            b.UndoneAt, b.UndoneBy is { } u ? names.GetValueOrDefault(u) : null, b.UndoReason, reconciled.GetValueOrDefault(b.Id), b.Version)).ToList();
    }

    /// <summary>Saldo do banco por dia no período, com o do sistema no fim do mesmo dia e a diferença (CA 3); a API vale sobre o OFX.</summary>
    private async Task<IReadOnlyList<BankBalanceDay>> BalanceDaysAsync(BankAccount account, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.BankStatementBalances.AsNoTracking().Where(b => b.BankAccountId == account.Id && b.Date >= from && b.Date <= to).OrderBy(b => b.Date).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return [];
        }

        // Saldo do sistema: o da véspera do primeiro dia mais o movimento de cada dia (duas consultas, qualquer que seja o período).
        var first = rows[0].Date;
        var running = await balances.BankBalanceAsync([account.Id], first.AddDays(-1), ct);
        var moves = await db.FinancialTransactions.AsNoTracking().Where(t => t.BankAccountId == account.Id && t.Date >= first && t.Date <= to)
            .GroupBy(t => t.Date).Select(g => new { g.Key, Total = g.Sum(t => t.Amount) }).ToDictionaryAsync(g => g.Key, g => g.Total, ct);
        var result = new List<BankBalanceDay>();
        var day = first;
        foreach (var group in rows.GroupBy(r => r.Date))
        {
            for (; day <= group.Key; day = day.AddDays(1))
            {
                running += moves.GetValueOrDefault(day);
            }

            var chosen = group.OrderBy(r => r.Source == StatementSource.Api ? 0 : 1).First();
            result.Add(new BankBalanceDay(group.Key, chosen.Balance, running, chosen.Balance - running, chosen.Source.ToString()));
        }

        return result;
    }

    private async Task<BankBalanceDay?> LatestBalanceAsync(BankAccount account, CancellationToken ct)
    {
        var latest = await db.BankStatementBalances.AsNoTracking().Where(b => b.BankAccountId == account.Id)
            .OrderByDescending(b => b.Date).ThenBy(b => b.Source == StatementSource.Api ? 0 : 1).FirstOrDefaultAsync(ct);
        if (latest is null)
        {
            return null;
        }

        var system = await balances.BankBalanceAsync([account.Id], latest.Date, ct);
        return new BankBalanceDay(latest.Date, latest.Balance, system, latest.Balance - system, latest.Source.ToString());
    }

    private async Task<OfxContingencyView?> ContingencyAsync(Guid accountId, DateOnly today, CancellationToken ct)
    {
        var contingency = await db.BankOfxContingencies.AsNoTracking().Where(c => c.BankAccountId == accountId && c.ValidUntil >= today)
            .OrderByDescending(c => c.GrantedAt).FirstOrDefaultAsync(ct);
        return contingency is null
            ? null
            : new OfxContingencyView(contingency.ValidUntil, contingency.GrantedBy is { } by ? await users.DisplayNameAsync(by, ct) : null, contingency.GrantedAt, contingency.Reason);
    }

    /// <summary>Conta do tipo Banco, não excluída (o filtro de exclusão lógica vale); caixa não tem extrato do banco.</summary>
    private Task<BankAccount?> AccountAsync(Guid accountId, CancellationToken ct) =>
        db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId && a.Kind == BankAccountKind.Bank, ct);

    private static Error NotFound() => Error.NotFound("bank_account_not_found", NotFoundMessage);

    /// <summary>Advisory lock da conta na transação aberta: duas importações da mesma conta esperam uma pela outra (CA 2).</summary>
    private async Task LockAccountAsync(Guid accountId, CancellationToken ct)
    {
        var key = "bank-statement:" + accountId.ToString("N");
        await db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM (SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))) AS l").ToListAsync(ct);
    }

    /// <summary>
    /// Grava o lote e as linhas novas sob o lock da conta: as que já existem na conta (mesma origem e mesmo id do banco; no OFX de
    /// contingência, também as da API pelo id do banco) contam como já importadas. Devolve as contagens.
    /// </summary>
    private async Task<(int New, int Duplicates)> InsertLinesAsync(BankStatementBatch batch, List<BankStatementLine> candidates, bool matchAnySource, CancellationToken ct)
    {
        var ids = candidates.Select(c => c.ExternalId).Distinct(StringComparer.Ordinal).ToList();
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in ids.Chunk(5000))
        {
            var query = db.BankStatementLines.AsNoTracking().Where(l => l.BankAccountId == batch.BankAccountId && l.Status != StatementLineStatus.Undone && chunk.Contains(l.ExternalId));
            if (!matchAnySource)
            {
                query = query.Where(l => l.Source == batch.Source);
            }

            existing.UnionWith(await query.Select(l => l.ExternalId).ToListAsync(ct));
        }

        var fresh = new List<BankStatementLine>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (!existing.Contains(candidate.ExternalId) && seen.Add(candidate.ExternalId))
            {
                fresh.Add(candidate);
            }
        }

        db.BankStatementLines.AddRange(fresh);
        return (fresh.Count, candidates.Count - fresh.Count);
    }

    private async Task NotifyOwnersAsync(string title, string link, CancellationToken ct)
    {
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, ImportNotification, title, link);
        }
    }

    private async Task<string> WhoAsync(CancellationToken ct) =>
        currentUser.UserId is { } me ? await users.DisplayNameAsync(me, ct) ?? "um usuário" : "o sistema";

    private static string Day(DateOnly date) => date.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture);

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    [LoggerMessage(Level = LogLevel.Information, Message = "Extrato do banco importado: conta {BankAccountId}, origem {Source}, período {From} a {To}, {New} novas, {Duplicates} já importadas, {Rejected} recusadas, {ElapsedMs} ms")]
    private static partial void LogImported(ILogger logger, Guid bankAccountId, string source, DateOnly? from, DateOnly? to, int @new, int duplicates, int rejected, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Extrato do banco não importado: conta {BankAccountId}, origem {Source}, motivo {Code}, {ElapsedMs} ms")]
    private static partial void LogRefused(ILogger logger, Guid bankAccountId, string source, string code, long elapsedMs);
}
