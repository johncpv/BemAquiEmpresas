using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Implantação financeira (FIN-00): data de corte, saldos das contas no fim desse dia, títulos em aberto lançados em lote
/// com origem <see cref="DocumentOrigin.Opening"/> (sem transação bancária), conferência e conclusão.
/// </summary>
public sealed partial class OpeningService(
    IFinanceDb db, IParties parties, IChartOfAccounts chart, INumberSequence numbers, IPermissionChecker permissions, ICurrentUser currentUser, TimeProvider clock)
{
    private DateOnly Today => BusinessTime.Today(clock);

    public async Task<OpeningStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        var setup = await db.OpeningSetups.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var cutover = setup?.CutoverDate;
        var accounts = await db.BankAccounts.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Name).ToListAsync(cancellationToken);
        var moved = (await db.FinancialTransactions.Select(t => t.BankAccountId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var sums = cutover is { } c
            ? await db.FinancialTransactions.Where(t => t.Date <= c).GroupBy(t => t.BankAccountId).Select(g => new { g.Key, Sum = g.Sum(t => t.Amount) }).ToDictionaryAsync(g => g.Key, g => g.Sum, cancellationToken)
            : [];
        var receivables = await db.Receivables.AsNoTracking().Where(r => r.Origin == DocumentOrigin.Opening && r.Status != ReceivableStatus.Cancelled)
            .GroupBy(r => r.PartyId).Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(r => r.Amount) }).ToListAsync(cancellationToken);
        var payables = await db.Payables.AsNoTracking().Where(p => p.Origin == DocumentOrigin.Opening && p.Status != PayableStatus.Cancelled)
            .GroupBy(p => p.PartyId).Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(p => p.Amount) }).ToListAsync(cancellationToken);
        var names = (await parties.FindManyAsync(receivables.Select(r => r.Key).Concat(payables.Select(p => p.Key)).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var lastMonth = new DateOnly(Today.Year, Today.Month, 1).AddDays(-1);
        return new OpeningStatus(
            cutover, lastMonth, setup?.CompletedAt, setup?.Notes, moved.Count > 0,
            accounts.Select(a => new OpeningAccount(a.Id, a.Name, a.OpeningBalance, a.OpeningDate, moved.Contains(a.Id), (a.OpeningBalance ?? 0) + sums.GetValueOrDefault(a.Id))).ToList(),
            receivables.Select(r => new OpeningPartyTotal(r.Key, names.GetValueOrDefault(r.Key, string.Empty), r.Count, r.Total)).OrderBy(r => r.PartyName, StringComparer.CurrentCulture).ToList(),
            payables.Select(p => new OpeningPartyTotal(p.Key, names.GetValueOrDefault(p.Key, string.Empty), p.Count, p.Total)).OrderBy(p => p.PartyName, StringComparer.CurrentCulture).ToList(),
            setup?.Version ?? 0);
    }

    /// <summary>Data de corte (CA 1 e 2): só com <c>finance.approve</c>, até hoje, e nunca depois do primeiro movimento.</summary>
    public async Task<Result<OpeningStatus>> SetCutoverAsync(CutoverInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("finance", "approve", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem define a data de corte é o Proprietário.");
        }

        if (input.CutoverDate > Today)
        {
            return ReceivableService.Invalid("cutoverDate", "A data de corte não pode ser futura.");
        }

        var setup = await SetupAsync(cancellationToken);
        if (setup.CompletedAt is { } done)
        {
            return Completed(done);
        }

        if (setup.CutoverDate != input.CutoverDate && await db.FinancialTransactions.AnyAsync(cancellationToken))
        {
            return Error.Conflict("cutover_locked", "A data de corte não muda depois do primeiro movimento.");
        }

        setup.CutoverDate = input.CutoverDate;
        await db.SaveChangesAsync(cancellationToken);
        return await StatusAsync(cancellationToken);
    }

    private async Task<OpeningSetup> SetupAsync(CancellationToken ct)
    {
        var setup = await db.OpeningSetups.FirstOrDefaultAsync(ct);
        if (setup is null)
        {
            setup = new OpeningSetup();
            db.OpeningSetups.Add(setup);
        }

        return setup;
    }

    private static Error Completed(DateTimeOffset at) =>
        Error.Conflict("opening_completed", $"A implantação foi concluída em {BusinessTime.ToBusiness(at):dd/MM/yyyy}.");
}
