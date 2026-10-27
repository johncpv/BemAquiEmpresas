using System.Diagnostics;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Statements;

public sealed partial class BankStatementImportService
{
    public const string NoApiMessage = "Esta conta não recebe o extrato pela API. Importe um arquivo OFX.";
    public const string ConcurrentMessage = "Outra importação desta conta terminou agora. Busque de novo.";

    /// <summary>
    /// "Buscar agora" (CA 1): quem tem <c>finance.edit</c>, no máximo uma vez a cada 5 min por conta (429 com o horário da próxima).
    /// As barreiras de efeito da BNK-01a valem: produção sem <c>bank_production_allowed</c> responde 409 <c>bank_production_not_allowed</c>.
    /// </summary>
    public async Task<Result<BankStatementImportResult>> FetchAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await AccountAsync(accountId, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (account.Provider == BankProvider.None)
        {
            return Error.Conflict("bank_statement_no_api", NoApiMessage);
        }

        var last = await db.BankStatementBatches.AsNoTracking().Where(b => b.BankAccountId == accountId && b.Source == StatementSource.Api)
            .OrderByDescending(b => b.ImportedAt).Select(b => (DateTimeOffset?)b.ImportedAt).FirstOrDefaultAsync(cancellationToken);
        if (last is { } at && at + Settings.FetchCooldown > clock.GetUtcNow())
        {
            var next = BusinessTime.ToBusiness(at + Settings.FetchCooldown);
            return Error.TooManyRequests("bank_statement_fetch_too_soon",
                $"O extrato desta conta foi buscado às {BusinessTime.ToBusiness(at):HH:mm}. Busque de novo a partir das {next:HH:mm}.");
        }

        return await ImportApiAsync(account, StatementTrigger.Manual, cancellationToken);
    }

    /// <summary>
    /// Job diário (CA 1, recorrente <c>inter-statement-import</c>): todas as contas com integração ativa. Cada conta é independente: a
    /// recusa de uma (produção sem a flag, certificado vencido, Inter fora do ar) fica no log e não para as outras.
    /// </summary>
    public async Task<int> RunScheduledAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await db.BankAccounts.AsNoTracking()
            .Where(a => a.IsActive && a.Kind == Contracts.BankAccountKind.Bank && a.Provider != BankProvider.None)
            .Join(db.BankIntegrations.AsNoTracking().Where(i => i.Status == BankIntegrationStatus.Active), a => a.Id, i => i.BankAccountId, (a, _) => a)
            .ToListAsync(cancellationToken);
        var imported = 0;
        foreach (var account in accounts)
        {
            var result = await ImportApiAsync(account, StatementTrigger.Scheduled, cancellationToken);
            if (result.IsSuccess)
            {
                imported++;
            }
        }

        return imported;
    }

    /// <summary>
    /// Busca desde a última linha da API (menos os dias de sobreposição; nunca antes da data de corte da FIN-00), em janelas, e grava
    /// as linhas novas e o saldo do banco dos últimos dias fechados e de hoje (CA 1 a 3). Nada é gravado se o banco recusar o extrato.
    /// </summary>
    private async Task<Result<BankStatementImportResult>> ImportApiAsync(BankAccount account, StatementTrigger trigger, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        var allowed = await integrations.EffectsAllowedAsync(account.Id, ct);
        if (!allowed.IsSuccess)
        {
            LogRefused(logger, account.Id, nameof(StatementSource.Api), allowed.Error!.Code, Elapsed(started));
            return allowed.Error.Type == ErrorType.NotFound ? Error.Conflict("bank_statement_no_api", NoApiMessage) : allowed.Error;
        }

        var provider = providers.FirstOrDefault(p => p.Provider == account.Provider);
        if (provider is null)
        {
            return Error.Conflict("bank_statement_no_api", NoApiMessage);
        }

        var today = BusinessTime.Today(clock);
        var cutover = await db.OpeningSetups.AsNoTracking().Select(s => s.CutoverDate).FirstOrDefaultAsync(ct);
        var lastLine = await db.BankStatementLines.AsNoTracking().Where(l => l.BankAccountId == account.Id && l.Source == StatementSource.Api && l.Status != StatementLineStatus.Undone)
            .MaxAsync(l => (DateOnly?)l.Date, ct);
        var from = lastLine is { } last ? last.AddDays(-Settings.OverlapDays) : today.AddDays(-Settings.InitialDays);
        if (cutover is { } cut && from < cut)
        {
            from = cut;
        }

        if (from > today)
        {
            from = today;
        }

        var fetched = new List<ProviderStatementLine>();
        var rejected = 0;
        var balanceRows = new List<(DateOnly Date, decimal Balance)>();
        var notes = new List<string>();
        using (var credentials = integrations.Open(allowed.Value))
        {
            for (var window = from; window <= today; window = window.AddDays(Settings.WindowDays))
            {
                var end = window.AddDays(Settings.WindowDays - 1) < today ? window.AddDays(Settings.WindowDays - 1) : today;
                var page = await provider.GetAsync(credentials, window, end, ct);
                if (!page.Ok)
                {
                    LogRefused(logger, account.Id, nameof(StatementSource.Api), page.Failure!.Kind.ToString(), Elapsed(started));
                    return Error.Unavailable("bank_statement_refused", page.Failure.Message + " " + page.Failure.Action) with
                    {
                        Data = new Dictionary<string, object?> { ["action"] = page.Failure.Action },
                    };
                }

                fetched.AddRange(page.Value!.Lines);
                rejected += page.Value.Rejected;
            }

            // Saldo dos últimos dias fechados e o de hoje (CA 3); falha no saldo não perde as linhas.
            var balanceFrom = today.AddDays(-Settings.BalanceDays) > from ? today.AddDays(-Settings.BalanceDays) : from;
            for (var day = balanceFrom; day <= today; day = day.AddDays(1))
            {
                var balance = await provider.GetBalanceAsync(credentials, day, ct);
                if (!balance.Ok)
                {
                    notes.Add("O saldo do banco não veio nesta busca: " + balance.Failure!.Message);
                    break;
                }

                balanceRows.Add((day, balance.Value));
            }
        }

        var beforeCutover = cutover is { } c ? fetched.Count(l => l.Date < c) : 0;
        if (beforeCutover > 0)
        {
            notes.Add($"{beforeCutover} {(beforeCutover == 1 ? "linha anterior" : "linhas anteriores")} à data de corte ficaram de fora.");
        }

        if (rejected > 0)
        {
            notes.Add($"{rejected} {(rejected == 1 ? "lançamento veio" : "lançamentos vieram")} do banco sem id, data ou valor e ficaram de fora.");
        }

        var now = clock.GetUtcNow();
        var batch = new BankStatementBatch
        {
            BankAccountId = account.Id, Source = StatementSource.Api, Trigger = trigger, PeriodFrom = from, PeriodTo = today, ImportedAt = now, ImportedBy = currentUser.UserId,
        };
        var sequence = 0;
        var candidates = fetched.Where(l => cutover is not { } cc || l.Date >= cc).Select(l => new BankStatementLine
        {
            BankAccountId = account.Id, BatchId = batch.Id, Date = l.Date, Amount = l.Amount, Description = Trim(l.Description, 300) ?? "(sem descrição)", Kind = l.Kind,
            CounterpartyName = Trim(l.CounterpartyName, 150), CounterpartyDocument = Document(l.CounterpartyDocument), ExternalId = l.ExternalId, Source = StatementSource.Api,
            BankVerified = true, Sequence = ++sequence, ImportedAt = now,
        }).ToList();

        int fresh, duplicates;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await LockAccountAsync(account.Id, ct);
            (fresh, duplicates) = await InsertLinesAsync(batch, candidates, matchAnySource: false, ct);
            batch.NewCount = fresh;
            batch.DuplicateCount = duplicates;
            batch.RejectedCount = rejected + beforeCutover;
            batch.Notes = notes.Count == 0 ? null : Trim(string.Join(' ', notes), 1000);
            db.BankStatementBatches.Add(batch);
            await UpsertBalancesAsync(account.Id, batch.Id, StatementSource.Api, balanceRows, now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, ExternalIndex))
        {
            LogRefused(logger, account.Id, nameof(StatementSource.Api), "concurrent", Elapsed(started));
            return Error.Conflict("bank_statement_concurrent", ConcurrentMessage);
        }

        await NotifyDifferenceAsync(account, balanceRows, today, ct);
        var elapsed = Elapsed(started);
        var rejectedTotal = batch.RejectedCount;
        LogImported(logger, account.Id, nameof(StatementSource.Api), from, today, fresh, duplicates, rejectedTotal, elapsed);
        var pending = await db.BankStatementLines.CountAsync(l => l.BankAccountId == account.Id && l.Status == StatementLineStatus.Pending, ct);
        return new BankStatementImportResult(batch.Id, fresh, duplicates, batch.RejectedCount, notes, from, today, pending, true);
    }

    /// <summary>Um saldo por conta, dia e origem: a importação seguinte atualiza o do mesmo dia.</summary>
    private async Task UpsertBalancesAsync(Guid accountId, Guid batchId, StatementSource source, List<(DateOnly Date, decimal Balance)> rows, DateTimeOffset now, CancellationToken ct)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var dates = rows.Select(r => r.Date).ToList();
        var existing = await db.BankStatementBalances.Where(b => b.BankAccountId == accountId && b.Source == source && dates.Contains(b.Date)).ToDictionaryAsync(b => b.Date, ct);
        foreach (var (date, balance) in rows)
        {
            if (existing.TryGetValue(date, out var row))
            {
                row.Balance = balance;
                row.BatchId = batchId;
                row.RecordedAt = now;
            }
            else
            {
                db.BankStatementBalances.Add(new BankStatementBalance { BankAccountId = accountId, Date = date, Balance = balance, Source = source, BatchId = batchId, RecordedAt = now });
            }
        }
    }

    /// <summary>
    /// CA 3: diferença entre o saldo do banco e o do sistema num dia de mais de 2 dias atrás avisa quem tem <c>finance.edit</c>, uma vez
    /// por conta e dia (<c>stmt-diff:{conta}:{data}</c>), com o link para a conciliação (a aba do extrato do banco).
    /// </summary>
    private async Task NotifyDifferenceAsync(BankAccount account, List<(DateOnly Date, decimal Balance)> rows, DateOnly today, CancellationToken ct)
    {
        var limit = today.AddDays(-(Settings.DifferenceAlertDays + 1));
        foreach (var (date, bank) in rows.Where(r => r.Date <= limit).OrderByDescending(r => r.Date))
        {
            var system = await balances.BankBalanceAsync([account.Id], date, ct);
            if (bank == system)
            {
                continue;
            }

            var title = $"O saldo da conta {account.Name} no banco em {Day(date)} difere do sistema em {ReceivableService.Money(bank - system)}. Concilie o extrato.";
            foreach (var user in await users.UsersWithPermissionAsync("finance.edit", ct))
            {
                await notifications.NotifyOnceAsync(user, DifferenceNotification, title, Link(account.Id), $"stmt-diff:{account.Id}:{date:yyyy-MM-dd}", ct);
            }

            break;
        }
    }

    private static string? Trim(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = new string(text.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return cleaned.Length > max ? cleaned[..max] : cleaned;
    }

    private static string? Document(string? document)
    {
        var digits = BrazilianDocuments.Normalize(document);
        return digits.Length is 11 or 14 ? digits : null;
    }
}
