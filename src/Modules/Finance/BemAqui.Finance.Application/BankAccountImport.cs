using BemAqui.Finance.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// <see cref="IBankAccountImport"/> (MIG-01b CA 15 e 16): cria pelo serviço da tela, sempre sem saldo; a atualização é do
/// sistema e não passa pela troca protegida de dados bancários (não há usuário na carga).
/// </summary>
public sealed class BankAccountImport(BankAccountService service, IFinanceDb db) : IBankAccountImport
{
    public async Task<Result<FinanceImportWrite>> CreateAsync(BankAccountImportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var created = await service.CreateAsync(Input(data), data.IsActive, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var account = await db.BankAccounts.FirstAsync(a => a.Id == created.Value.Id, cancellationToken);
        account.ImportSource = data.ImportSource;
        await db.SaveChangesAsync(cancellationToken);
        return new FinanceImportWrite(account.Id, await VersionAsync(account.Id, cancellationToken) ?? 0);
    }

    public async Task<Result<FinanceImportWrite>> UpdateAsync(Guid accountId, BankAccountImportData data, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        var account = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null)
        {
            return Error.NotFound("bank_account_not_found", "Conta não encontrada.");
        }

        if (FinanceValidation.BankAccount(Input(data)) is { } invalid)
        {
            return invalid;
        }

        account.Name = data.Name.Trim();
        account.Kind = data.Kind;
        (account.BankCode, account.Agency, account.Account) = data.Kind == BankAccountKind.Bank ? (data.BankCode, data.Agency, data.Account) : (null, null, null);
        account.IsActive = data.IsActive || account.IsDefault;
        account.ImportSource ??= data.ImportSource;
        await db.SaveChangesAsync(cancellationToken);
        return new FinanceImportWrite(account.Id, await VersionAsync(account.Id, cancellationToken) ?? 0);
    }

    public async Task<BankAccountImportData?> CurrentAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var a = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        return a is null ? null : new BankAccountImportData(a.Name, a.Kind, a.BankCode, a.Agency, a.Account, a.IsActive, a.ImportSource ?? string.Empty);
    }

    public Task<long?> VersionAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        db.Database.SqlQuery<long?>($"SELECT xmin::text::bigint AS \"Value\" FROM finance.bank_accounts WHERE id = {accountId} AND NOT is_deleted").SingleOrDefaultAsync(cancellationToken);

    private static BankAccountInput Input(BankAccountImportData d) => new(d.Name, d.Kind, d.BankCode, d.Agency, d.Account, null, null);
}
