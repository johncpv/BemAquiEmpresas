using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Plano de contas gerencial (CAD-05a CA 4 a 15). A conta pai vira sintética quando ganha a primeira filha ativa; com
/// lançamento (<see cref="IAccountUsageProvider"/>), a conta não é excluída nem vira agrupadora (D10).
/// </summary>
public sealed class AccountService(IFinanceDb db, IEnumerable<IAccountUsageProvider> usages, TimeProvider clock) : IChartOfAccounts
{
    public const string CodeIndex = "ix_chart_of_accounts_code";

    public async Task<IReadOnlyList<AccountDetail>> ListAsync(CancellationToken cancellationToken = default) =>
        (await db.Accounts.AsNoTracking().ToListAsync(cancellationToken)).OrderBy(a => TreeCode.SortKey(a.Code)).Select(ToDetail).ToList();

    public async Task<Result<AccountDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken) is { } account ? ToDetail(account) : NotFound();

    /// <summary>Próximo código e o tipo e o grupo do pai (CA 5).</summary>
    public async Task<Result<AccountSuggestion>> SuggestAsync(Guid? parentId, CancellationToken cancellationToken = default)
    {
        Account? parent = null;
        if (parentId is { } pid && (parent = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == pid, cancellationToken)) is null)
        {
            return NotFound();
        }

        // Os excluídos também contam: o código deles continua no histórico.
        var siblings = await db.Accounts.IgnoreQueryFilters().AsNoTracking().Where(a => a.ParentId == parentId).Select(a => a.Code).ToListAsync(cancellationToken);
        return new AccountSuggestion(TreeCode.Next(parent?.Code, siblings), parent?.Type, parent?.DreGroup);
    }

    public async Task<Result<AccountDetail>> CreateAsync(AccountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var account = new Account { CreatedAt = clock.GetUtcNow() };
        if (await ValidateAsync(null, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        Apply(account, input);
        db.Accounts.Add(account);
        return await SaveAsync(account, [input.ParentId], cancellationToken);
    }

    public async Task<Result<AccountDetail>> UpdateAsync(Guid id, AccountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (await ValidateAsync(account, input, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        var oldParent = account.ParentId;
        Apply(account, input);
        return await SaveAsync(account, [oldParent, input.ParentId], cancellationToken);
    }

    /// <summary>Inativar (CA 14): a sintética só com as filhas inativas; reativar exige o pai ativo.</summary>
    public async Task<Result<AccountDetail>> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (!active)
        {
            var activeChildren = await db.Accounts.Where(a => a.ParentId == id && a.IsActive).Select(a => a.Code).ToListAsync(cancellationToken);
            if (activeChildren.Count > 0)
            {
                return Error.Conflict("account_has_active_children", "Inative antes as contas filhas: " + string.Join(", ", activeChildren.OrderBy(TreeCode.SortKey)) + ".");
            }
        }
        else if (account.ParentId is { } pid && await db.Accounts.AnyAsync(a => a.Id == pid && !a.IsActive, cancellationToken))
        {
            return Error.Conflict("account_parent_inactive", "Reative antes a conta pai.");
        }

        account.IsActive = active;
        return await SaveAsync(account, [account.ParentId], cancellationToken);
    }

    /// <summary>Excluir (CA 12 e 13): só sem filhas e sem lançamento.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (await db.Accounts.AnyAsync(a => a.ParentId == id, cancellationToken))
        {
            return Error.Conflict("account_has_children", "A conta tem contas filhas. Exclua ou mova as filhas antes.");
        }

        if (await HasEntriesAsync(id, cancellationToken))
        {
            return Error.Conflict("account_in_use", "Conta com lançamentos não pode ser excluída. Inative-a.");
        }

        db.Accounts.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
        await RefreshAsync([account.ParentId], cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<AccountOption>> PostableAsync(AccountType? type = null, CancellationToken cancellationToken = default) =>
        (await db.Accounts.AsNoTracking().Where(a => a.IsActive && a.AcceptsEntries && (type == null || a.Type == type)).ToListAsync(cancellationToken))
            .OrderBy(a => TreeCode.SortKey(a.Code)).Select(ToOption).ToList();

    public async Task<IReadOnlyList<AccountOption>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return (await db.Accounts.IgnoreQueryFilters().AsNoTracking().Where(a => ids.Contains(a.Id)).ToListAsync(cancellationToken)).Select(ToOption).ToList();
    }

    private async Task<Error?> ValidateAsync(Account? current, AccountInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 100)
        {
            return TreeValidation.Invalid("name", "Informe o nome da conta, com até 100 caracteres.");
        }

        if (!Enum.IsDefined(input.Type) || !Enum.IsDefined(input.DreGroup))
        {
            return TreeValidation.Invalid("type", "Escolha o tipo e o grupo da DRE.");
        }

        var (error, parent) = await TreeValidation.ValidateAsync(db.Accounts, current, input.Code?.Trim(), input.ParentId, TreeValidation.Account, ct);
        if (error is not null)
        {
            return error;
        }

        if (parent is not null && parent.Type != input.Type)
        {
            return TreeValidation.Invalid("type", "A conta precisa ter o mesmo tipo da conta pai.");
        }

        if (current is not null && current.Type != input.Type && await db.Accounts.AnyAsync(a => a.ParentId == current.Id, ct))
        {
            return TreeValidation.Invalid("type", "Troque antes o tipo das contas filhas.");
        }

        // A analítica com lançamento não vira agrupadora (CA 10).
        if (parent is not null && parent.AcceptsEntries && parent.Id != current?.ParentId && await HasEntriesAsync(parent.Id, ct))
        {
            return Error.Conflict("account_has_entries", "Esta conta já tem lançamentos e não pode virar agrupadora. Crie a conta em outro grupo.");
        }

        return null;
    }

    private static void Apply(Account account, AccountInput input)
    {
        account.Code = input.Code.Trim();
        account.Name = input.Name.Trim();
        account.Type = input.Type;
        account.DreGroup = input.DreGroup;
        account.ParentId = input.ParentId;
    }

    private async Task<Result<AccountDetail>> SaveAsync(Account account, IEnumerable<Guid?> parents, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, CodeIndex))
        {
            db.Accounts.Entry(account).State = EntityState.Detached;
            return Error.Conflict("tree_code_taken", $"Já existe a conta {account.Code}.");
        }

        await RefreshAsync(parents, ct);
        return ToDetail(account);
    }

    /// <summary>Sintética × analítica derivada (CA 9): o pai aceita lançamento só sem filhas ativas.</summary>
    private async Task RefreshAsync(IEnumerable<Guid?> parentIds, CancellationToken ct)
    {
        foreach (var id in parentIds.OfType<Guid>().Distinct())
        {
            var parent = await db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (parent is null)
            {
                continue;
            }

            var accepts = !await db.Accounts.AnyAsync(a => a.ParentId == id && a.IsActive, ct);
            if (parent.AcceptsEntries != accepts)
            {
                parent.AcceptsEntries = accepts;
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task<bool> HasEntriesAsync(Guid id, CancellationToken ct)
    {
        foreach (var usage in usages)
        {
            if (await usage.HasEntriesAsync(id, ct))
            {
                return true;
            }
        }

        return false;
    }

    public static AccountDetail ToDetail(Account a) => new(a.Id, a.Code, a.Name, a.Type, a.DreGroup, a.ParentId, a.AcceptsEntries, a.IsActive, a.SeedKey);

    private static AccountOption ToOption(Account a) => new(a.Id, a.Code, a.Name, a.Type, a.DreGroup, a.AcceptsEntries, a.IsActive);

    private static Error NotFound() => Error.NotFound("account_not_found", "Conta não encontrada.");
}
