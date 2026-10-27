using System.Diagnostics;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Statements;

public sealed partial class BankStatementImportService
{
    public const string OfxBlockedCode = "ofx_blocked_api_account";
    public const string OfxBlockedMessage = "Esta conta recebe o extrato direto do Inter. O arquivo só entra como contingência.";

    /// <summary>
    /// "Importar extrato" por arquivo OFX (CA 4 a 7): conta com API só com a contingência liberada (409 <c>ofx_blocked_api_account</c>);
    /// o arquivo passa pela triagem do dono <c>BankStatementBatch</c> (extensão, magic bytes e antivírus) ANTES da leitura; o parser é
    /// restrito e com tempo limite; a conta do arquivo é conferida com a escolhida; o mesmo arquivo (SHA-256) é recusado inteiro, o
    /// <c>FITID</c> repetido no arquivo recusa o arquivo e o já importado na conta é ignorado. O Proprietário é avisado de toda importação.
    /// </summary>
    public async Task<Result<BankStatementImportResult>> ImportOfxAsync(Guid accountId, string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var started = Stopwatch.GetTimestamp();
        var account = await AccountAsync(accountId, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (!account.IsActive)
        {
            return Error.Validation("bank_account_inactive", "Reative a conta bancária antes de importar o extrato.");
        }

        var today = BusinessTime.Today(clock);
        var api = account.Provider != BankProvider.None;
        if (api && await ContingencyAsync(account.Id, today, cancellationToken) is null)
        {
            LogRefused(logger, account.Id, nameof(StatementSource.Ofx), OfxBlockedCode, Elapsed(started));
            return Error.Conflict(OfxBlockedCode, OfxBlockedMessage);
        }

        if (content.Length > Settings.OfxMaxBytes)
        {
            return Error.Validation("ofx_size", OfxParser.TooLarge(Settings.OfxLimits), new Dictionary<string, string[]> { ["file"] = [OfxParser.TooLarge(Settings.OfxLimits)] });
        }

        // Antivírus e allowlist do dono antes de qualquer leitura do conteúdo (CA 4).
        var screened = await attachments.ScreenAsync(AttachmentOwner, fileName, content, cancellationToken);
        if (!screened.IsSuccess)
        {
            LogRefused(logger, account.Id, nameof(StatementSource.Ofx), screened.Error!.Code, Elapsed(started));
            return screened.Error;
        }

        var parsed = OfxParser.Parse(content, Settings.OfxLimits, cancellationToken);
        if (!parsed.IsSuccess)
        {
            LogRefused(logger, account.Id, nameof(StatementSource.Ofx), parsed.Error!.Code, Elapsed(started));
            return parsed.Error;
        }

        var statement = parsed.Value;
        if (AccountCheck(account, statement) is { } wrong)
        {
            LogRefused(logger, account.Id, nameof(StatementSource.Ofx), wrong.Code, Elapsed(started));
            return wrong;
        }

        var repeated = statement.Transactions.Where(t => t.FitId is { Length: > 0 }).GroupBy(t => t.FitId, StringComparer.Ordinal).Count(g => g.Count() > 1);
        if (repeated > 0)
        {
            const string message = "O arquivo repete o id de lançamento (FITID) e foi recusado inteiro. Exporte o extrato de novo pelo internet banking.";
            LogRefused(logger, account.Id, nameof(StatementSource.Ofx), "ofx_duplicate_fitid", Elapsed(started));
            return Error.Validation("ofx_duplicate_fitid", message, new Dictionary<string, string[]> { ["file"] = [message] });
        }

        var notes = new List<string>();
        var withoutFitId = statement.Transactions.Count(t => t.FitId is not { Length: > 0 });
        if (withoutFitId > 0)
        {
            notes.Add($"{withoutFitId} {(withoutFitId == 1 ? "lançamento veio" : "lançamentos vieram")} sem FITID: o sistema usou data, valor, descrição e ordem no arquivo para não duplicar.");
        }

        var cutover = await db.OpeningSetups.AsNoTracking().Select(s => s.CutoverDate).FirstOrDefaultAsync(cancellationToken);
        var beforeCutover = cutover is { } cut ? statement.Transactions.Count(t => t.Date < cut) : 0;
        if (beforeCutover > 0)
        {
            notes.Add($"{beforeCutover} {(beforeCutover == 1 ? "linha anterior" : "linhas anteriores")} à data de corte ({Day(cutover!.Value)}) ficaram de fora.");
        }

        if (api)
        {
            notes.Add("Contingência: as linhas não são verificadas pelo banco.");
        }

        var dates = statement.Transactions.Select(t => t.Date).ToList();
        var now = clock.GetUtcNow();
        var batch = new BankStatementBatch
        {
            BankAccountId = account.Id, Source = StatementSource.Ofx, Trigger = StatementTrigger.Upload,
            PeriodFrom = statement.Start ?? (dates.Count > 0 ? dates.Min() : null), PeriodTo = statement.End ?? (dates.Count > 0 ? dates.Max() : null),
            FileName = screened.Value.FileName, FileSha256 = screened.Value.Sha256, BankVerified = !api, ImportedAt = now, ImportedBy = currentUser.UserId,
        };
        var candidates = statement.Transactions.Where(t => cutover is not { } c || t.Date >= c).Select(t => new BankStatementLine
        {
            BankAccountId = account.Id, BatchId = batch.Id, Date = t.Date, Amount = t.Amount, Description = Trim(t.Memo ?? t.Name, 300) ?? "(sem descrição)",
            Kind = StatementKinds.FromOfx(t.TransactionType, t.Memo, t.Name), CounterpartyName = t.Memo is not null && t.Name is not null && t.Name != t.Memo ? Trim(t.Name, 150) : null,
            ExternalId = OfxParser.ExternalId(t), Source = StatementSource.Ofx, BankVerified = !api, Sequence = t.Sequence, ImportedAt = now,
        }).ToList();

        int fresh, duplicates;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await LockAccountAsync(account.Id, cancellationToken);
            var previous = await db.BankStatementBatches.AsNoTracking()
                .Where(b => b.BankAccountId == account.Id && b.FileSha256 == batch.FileSha256 && b.UndoneAt == null).FirstOrDefaultAsync(cancellationToken);
            if (previous is not null)
            {
                LogRefused(logger, account.Id, nameof(StatementSource.Ofx), "ofx_already_imported", Elapsed(started));
                return await AlreadyImportedAsync(previous, cancellationToken);
            }

            (fresh, duplicates) = await InsertLinesAsync(batch, candidates, matchAnySource: api, cancellationToken);
            batch.NewCount = fresh;
            batch.DuplicateCount = duplicates;
            batch.RejectedCount = beforeCutover;
            batch.Notes = notes.Count == 0 ? null : Trim(string.Join(' ', notes), 1000);
            var attachment = await attachments.StoreScreenedAsync(AttachmentOwner, batch.Id, screened.Value, cancellationToken);
            batch.AttachmentId = attachment.Id;
            db.BankStatementBatches.Add(batch);
            if (statement.LedgerBalance is { } ledger && statement.LedgerDate is { } ledgerDate && (cutover is not { } lc || ledgerDate >= lc))
            {
                await UpsertBalancesAsync(account.Id, batch.Id, StatementSource.Ofx, [(ledgerDate, ledger)], now, cancellationToken);
            }

            // CA 7: o Proprietário fica sabendo de toda importação de OFX, com o link para o lote.
            var period = batch.PeriodFrom is { } pf && batch.PeriodTo is { } pt ? $", período {Day(pf)} a {Day(pt)}" : string.Empty;
            await NotifyOwnersAsync($"{await WhoAsync(cancellationToken)} importou o extrato da conta {account.Name} ({fresh} {(fresh == 1 ? "linha" : "linhas")}{period}).",
                BatchLink(account.Id, batch.Id), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, FileIndex))
        {
            db.Entry(batch).State = EntityState.Detached;
            var previous = await db.BankStatementBatches.AsNoTracking().FirstAsync(b => b.BankAccountId == account.Id && b.FileSha256 == batch.FileSha256 && b.UndoneAt == null, cancellationToken);
            return await AlreadyImportedAsync(previous, cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, ExternalIndex))
        {
            return Error.Conflict("bank_statement_concurrent", ConcurrentMessage);
        }

        var elapsed = Elapsed(started);
        var (periodFrom, periodTo, rejectedTotal) = (batch.PeriodFrom, batch.PeriodTo, batch.RejectedCount);
        LogImported(logger, account.Id, nameof(StatementSource.Ofx), periodFrom, periodTo, fresh, duplicates, rejectedTotal, elapsed);
        var pending = await db.BankStatementLines.CountAsync(l => l.BankAccountId == account.Id && l.Status == StatementLineStatus.Pending, cancellationToken);
        return new BankStatementImportResult(batch.Id, fresh, duplicates, batch.RejectedCount, notes, batch.PeriodFrom, batch.PeriodTo, pending, batch.BankVerified);
    }

    /// <summary>
    /// "Liberar OFX nesta conta" (CA 6, D13): só em conta com API, por quem tem <c>finance.approve</c> (no endpoint), com motivo e prazo
    /// de até 30 dias; os membros do Proprietário são avisados e a liberação fica na auditoria.
    /// </summary>
    public async Task<Result<OfxContingencyView>> GrantContingencyAsync(Guid accountId, OfxContingencyInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var account = await AccountAsync(accountId, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (account.Provider == BankProvider.None)
        {
            return Error.Conflict("ofx_contingency_not_needed", "Esta conta não recebe o extrato pela API: o arquivo OFX já entra sem liberação.");
        }

        var reason = input.Reason?.Trim() ?? string.Empty;
        if (reason.Length is 0 or > 200)
        {
            return Invalid("reason", "Informe o motivo da contingência (até 200 caracteres).");
        }

        var today = BusinessTime.Today(clock);
        if (input.ValidUntil < today || input.ValidUntil > today.AddDays(Settings.ContingencyMaxDays))
        {
            return Invalid("validUntil", $"Escolha um prazo de hoje até {Day(today.AddDays(Settings.ContingencyMaxDays))} ({Settings.ContingencyMaxDays} dias).");
        }

        var contingency = new BankOfxContingency { BankAccountId = account.Id, GrantedBy = currentUser.UserId, GrantedAt = clock.GetUtcNow(), Reason = reason, ValidUntil = input.ValidUntil };
        db.BankOfxContingencies.Add(contingency);
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, ContingencyNotification, $"{await WhoAsync(cancellationToken)} liberou o OFX na conta {account.Name} até {Day(input.ValidUntil)}. Motivo: {reason}", Link(account.Id));
        }

        await db.SaveChangesAsync(cancellationToken);
        return new OfxContingencyView(contingency.ValidUntil, currentUser.UserId is { } me ? await users.DisplayNameAsync(me, cancellationToken) : null, contingency.GrantedAt, contingency.Reason);
    }

    /// <summary>
    /// Conta do arquivo (<c>BANKID</c>/<c>ACCTID</c>) contra a escolhida (CA 4): o banco pelo código COMPE; a conta pelos dígitos, com ou
    /// sem o dígito verificador e com ou sem a agência na frente (bancos exportam dos dois jeitos).
    /// </summary>
    internal static Error? AccountCheck(BankAccount account, OfxStatement statement)
    {
        var file = Digits(statement.AccountId);
        if (file.Length == 0)
        {
            return Invalid("file", "O arquivo não traz a conta do extrato (ACCTID). Exporte de novo pelo internet banking.");
        }

        var registered = Digits(account.Account);
        if (registered.Length == 0)
        {
            return Invalid("file", "Cadastre o número da conta desta conta bancária para o sistema conferir o extrato.");
        }

        var final = file.Length > 4 ? file[^4..] : file;
        var wrong = Error.Validation("bank_statement_wrong_account", $"Este extrato é da conta final {final}, não da conta escolhida.",
            new Dictionary<string, string[]> { ["file"] = [$"Este extrato é da conta final {final}, não da conta escolhida."] });
        var bank = Digits(statement.BankId);
        if (bank.Length > 0 && Digits(account.BankCode) is { Length: > 0 } code && int.Parse(bank[^Math.Min(bank.Length, 9)..], System.Globalization.CultureInfo.InvariantCulture) != int.Parse(code, System.Globalization.CultureInfo.InvariantCulture))
        {
            return wrong;
        }

        var accounts = new[] { registered, registered.Length > 1 ? registered[..^1] : registered };
        var agency = Digits(account.Agency);
        var prefixes = new[] { string.Empty, agency, agency.Length > 1 ? agency[..^1] : agency }.Distinct().ToList();
        return accounts.Any(a => prefixes.Any(p => Same(p + a, file))) ? null : wrong;
    }

    private static bool Same(string left, string right) => left.TrimStart('0') == right.TrimStart('0') && left.TrimStart('0').Length > 0;

    private static string Digits(string? text) => new((text ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    private static Error Invalid(string field, string message) => Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    private async Task<Error> AlreadyImportedAsync(BankStatementBatch previous, CancellationToken ct)
    {
        var who = previous.ImportedBy is { } by ? await users.DisplayNameAsync(by, ct) ?? "um usuário" : "o sistema";
        var when = BusinessTime.ToBusiness(previous.ImportedAt).ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture);
        return Error.Conflict("ofx_already_imported", $"Este arquivo já foi importado em {when} por {who}.") with
        {
            Data = new Dictionary<string, object?> { ["batchId"] = previous.Id },
        };
    }
}
