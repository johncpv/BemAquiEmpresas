using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public sealed partial class BankAccountService
{
    /// <summary>Inativar ou reativar (D10). A conta padrão não é inativada (CA 7).</summary>
    public async Task<Result<BankAccountDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var account = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (!active && account.IsDefault)
        {
            return Error.Conflict("bank_account_is_default", "Escolha outra conta padrão antes.");
        }

        account.IsActive = active;
        return await SaveAsync(account, false, cancellationToken);
    }

    /// <summary>Exclusão lógica só sem movimentação (CA 8); a padrão não é excluída (CA 7).</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (account.IsDefault)
        {
            return Error.Conflict("bank_account_is_default", "Escolha outra conta padrão antes.");
        }

        if (await HasMovementAsync(id, cancellationToken))
        {
            return Error.Conflict("bank_account_in_use", "Conta com movimentação não pode ser excluída. Inative-a.");
        }

        db.BankAccounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>
    /// Grava; com <paramref name="makeDefault"/>, desmarca a padrão anterior e marca esta na mesma transação, em dois comandos,
    /// porque o índice único parcial é checado a cada um (CA 6). Duas gravações simultâneas: só uma vira padrão.
    /// </summary>
    private async Task<Result<BankAccountDetail>> SaveAsync(BankAccount account, bool makeDefault, CancellationToken cancellationToken)
    {
        if (makeDefault && !account.IsActive)
        {
            return Error.Conflict("bank_account_inactive", "Reative a conta antes de torná-la padrão.");
        }

        try
        {
            if (makeDefault)
            {
                // Dentro de uma transação de fora (ex.: a importação, MIG-01b), usa a dela.
                await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
                foreach (var previous in await db.BankAccounts.Where(a => a.IsDefault && a.Id != account.Id).ToListAsync(cancellationToken))
                {
                    previous.IsDefault = false;
                }

                await db.SaveChangesAsync(cancellationToken);
                account.IsDefault = true;
                await db.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
            }
            else
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, DefaultIndex))
        {
            return Error.Conflict("bank_account_default_race", "Outra conta acabou de virar padrão. Atualize a tela e tente de novo.");
        }

        return ToDetail(account, await CanSeeFullAsync(cancellationToken));
    }
}
