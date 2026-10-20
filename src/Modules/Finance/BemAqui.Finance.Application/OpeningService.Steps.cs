using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class OpeningService
{
    /// <summary>Saldos no fim do dia da data de corte (CA 3 e 4): conta com movimento não troca o saldo.</summary>
    public async Task<Result<OpeningStatus>> SaveBalancesAsync(OpeningBalancesInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var setup = await SetupAsync(cancellationToken);
        if (setup.CompletedAt is { } done)
        {
            return Completed(done);
        }

        if (setup.CutoverDate is not { } cutover)
        {
            return Error.Conflict("cutover_missing", "Defina a data de corte antes.");
        }

        var ids = input.Lines.Select(l => l.BankAccountId).ToList();
        var accounts = await db.BankAccounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var line = input.Lines[i];
            if (!accounts.TryGetValue(line.BankAccountId, out var account))
            {
                return ReceivableService.Invalid($"lines[{i}].bankAccountId", "Conta não encontrada.");
            }

            if (account.OpeningBalance == line.Balance && (line.Balance is null || account.OpeningDate == cutover))
            {
                continue;
            }

            if (await db.FinancialTransactions.AnyAsync(t => t.BankAccountId == account.Id, cancellationToken))
            {
                return Error.Conflict("bank_account_has_movement", $"A conta {account.Name} já tem movimento. Corrija com um lançamento.");
            }

            account.OpeningBalance = line.Balance;
            account.OpeningDate = line.Balance is null ? null : cutover;
        }

        await db.SaveChangesAsync(cancellationToken);
        return await StatusAsync(cancellationToken);
    }

    /// <summary>Conclusão (CA 10): com <c>finance.approve</c>; depois disso, nada de novo título de implantação.</summary>
    public async Task<Result<OpeningStatus>> CompleteAsync(CompleteOpeningInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await permissions.HasPermissionAsync("finance", "approve", cancellationToken))
        {
            return Error.Forbidden(Error.Codes.Forbidden, "Quem conclui a implantação é o Proprietário.");
        }

        var setup = await SetupAsync(cancellationToken);
        if (setup.CompletedAt is { } done)
        {
            return Completed(done);
        }

        if (setup.CutoverDate is null)
        {
            return Error.Conflict("cutover_missing", "Defina a data de corte antes.");
        }

        if (input.Notes is { Length: > 500 })
        {
            return ReceivableService.Invalid("notes", "Escreva a observação com até 500 caracteres.");
        }

        db.Entry(setup).Property(s => s.Version).OriginalValue = input.Version;
        setup.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        setup.CompletedAt = clock.GetUtcNow();
        setup.CompletedBy = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken);
        return await StatusAsync(cancellationToken);
    }

    /// <summary>Implantação ainda aberta: o cancelamento de título de implantação não pede o limite (CA 8).</summary>
    public async Task<bool> IsOpenAsync(CancellationToken cancellationToken = default) =>
        !await db.OpeningSetups.AnyAsync(s => s.CompletedAt != null, cancellationToken);
}
