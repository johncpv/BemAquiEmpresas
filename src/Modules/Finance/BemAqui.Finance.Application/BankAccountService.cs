using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Contas bancárias e caixas (CAD-05b CA 1 a 10). A primeira conta nasce padrão e só uma é padrão por vez (índice único
/// parcial); com movimentação, o saldo inicial trava e a conta não é excluída. Trocar banco, agência ou conta exige
/// <c>finance.approve</c> (o Proprietário tem) e avisa todos os membros do Proprietário (CA 9).
/// </summary>
public sealed partial class BankAccountService(
    IFinanceDb db,
    IEnumerable<IBankAccountUsageProvider> usages,
    IPermissionChecker permissions,
    INotificationService notifications,
    IUserDirectory users,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string DefaultIndex = "ix_bank_accounts_default";
    public const string NotificationType = "finance.bank_data_changed";

    public async Task<PagedList<BankAccountDetail>> ListAsync(BankAccountListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var full = await CanSeeFullAsync(cancellationToken);
        var accounts = db.BankAccounts.AsNoTracking().Where(a => query.IncludeInactive || a.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = "%" + query.Q.Trim() + "%";
            accounts = accounts.Where(a => EF.Functions.ILike(a.Name, term));
        }

        var result = await accounts.OrderByDescending(a => a.IsDefault).ThenBy(a => a.Name).ToPagedListAsync(page, cancellationToken);
        return new PagedList<BankAccountDetail>(result.Items.Select(a => ToDetail(a, full)).ToList(), result.Page, result.PageSize, result.Total);
    }

    public async Task<Result<BankAccountDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var account = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return account is null ? NotFound() : ToDetail(account, await CanSeeFullAsync(cancellationToken));
    }

    public async Task<Result<BankAccountDetail>> CreateAsync(BankAccountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (FinanceValidation.BankAccount(input) is { } invalid)
        {
            return invalid;
        }

        var account = new BankAccount { CreatedAt = clock.GetUtcNow() };
        Apply(account, input);
        account.OpeningBalance = input.OpeningBalance;
        account.OpeningDate = input.OpeningDate;
        // A primeira conta ativa já nasce padrão (CA 6).
        var makeDefault = input.IsDefault || !await db.BankAccounts.AnyAsync(a => a.IsActive && a.IsDefault, cancellationToken);
        db.BankAccounts.Add(account);
        return await SaveAsync(account, makeDefault, cancellationToken);
    }

    public async Task<Result<BankAccountDetail>> UpdateAsync(Guid id, BankAccountInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var account = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        if (FinanceValidation.BankAccount(input) is { } invalid)
        {
            return invalid;
        }

        var (bankCode, agency, number) = BankFields(input);
        var bankChanged = account.BankDataDiffers(bankCode, agency, number);
        if (bankChanged && !await permissions.HasPermissionAsync("finance", StandardActions.Approve, cancellationToken))
        {
            return Error.Forbidden("forbidden", "Trocar banco, agência ou conta exige ser do grupo Proprietário ou poder aprovar no financeiro.");
        }

        if ((account.OpeningBalance != input.OpeningBalance || account.OpeningDate != input.OpeningDate) && await HasMovementAsync(id, cancellationToken))
        {
            return Error.Conflict("bank_account_has_movement", "A conta já tem movimentação. Ajuste o saldo por um lançamento.");
        }

        Apply(account, input);
        account.OpeningBalance = input.OpeningBalance;
        account.OpeningDate = input.OpeningDate;
        if (bankChanged)
        {
            await NotifyOwnersAsync(account, cancellationToken);
        }

        return await SaveAsync(account, input.IsDefault && !account.IsDefault, cancellationToken);
    }

    private static (string? BankCode, string? Agency, string? Account) BankFields(BankAccountInput input) =>
        input.Kind == BankAccountKind.Bank ? (input.BankCode, input.Agency?.Trim().ToUpperInvariant(), input.Account?.Trim().ToUpperInvariant()) : (null, null, null);

    private static void Apply(BankAccount account, BankAccountInput input)
    {
        account.Name = input.Name.Trim();
        account.Kind = input.Kind;
        (account.BankCode, account.Agency, account.Account) = BankFields(input);
    }

    private async Task NotifyOwnersAsync(BankAccount account, CancellationToken cancellationToken)
    {
        var who = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, cancellationToken) ?? "um usuário" : "o sistema";
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, NotificationType, $"Dados da conta {account.Name} alterados por {who}", "/app/financeiro/contas?conta=" + account.Id);
        }
    }

    private async Task<bool> HasMovementAsync(Guid id, CancellationToken cancellationToken)
    {
        foreach (var usage in usages)
        {
            if (await usage.HasMovementAsync(id, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask<bool> CanSeeFullAsync(CancellationToken cancellationToken) =>
        await permissions.HasPermissionAsync("finance", StandardActions.Edit, cancellationToken);

    private static Error NotFound() => Error.NotFound("bank_account_not_found", "Conta não encontrada.");

    public static BankAccountDetail ToDetail(BankAccount a, bool full) => new(
        a.Id, a.Name, a.Kind, a.BankCode, Banks.Find(a.BankCode)?.Name,
        full ? a.Agency : FinanceValidation.MaskAgency(a.Agency),
        full ? a.Account : FinanceValidation.MaskAccount(a.Account),
        a.OpeningBalance, a.OpeningDate, a.IsActive, a.IsDefault, a.ImportSource, !full, a.CreatedAt);
}
