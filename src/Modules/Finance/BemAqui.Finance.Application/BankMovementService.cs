using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Transferências e lançamentos sem OP (FIN-04 CA 5 a 14): os únicos casos de uso que criam transação <c>Transfer</c> e
/// <c>BankEntry</c> (teste de arquitetura). Recebimento e pagamento não são editados nem estornados pelo extrato.
/// </summary>
public sealed partial class BankMovementService(
    IFinanceDb db, IChartOfAccounts chart, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, INotificationService notifications,
    IFinanceParameters parameters, TimeProvider clock)
{
    public const string NotificationType = "finance.bank";

    private DateOnly Today => BusinessTime.Today(clock);

    private async Task<Error?> DateErrorAsync(DateOnly date, string? backdateReason, IEnumerable<BankAccount> accounts, CancellationToken ct)
    {
        if (date > Today)
        {
            return ReceivableService.Invalid("date", "A data não pode ser futura.");
        }

        if (accounts.FirstOrDefault(a => a.OpeningDate is { } opening && date < opening) is { } early)
        {
            return ReceivableService.Invalid("date", $"A data é anterior ao saldo inicial de {early.Name} ({early.OpeningDate:dd/MM/yyyy}).");
        }

        var limits = await parameters.LimitsAsync(ct);
        return date < Today.AddDays(-limits.BackdateDays) && string.IsNullOrWhiteSpace(backdateReason)
            ? ReceivableService.Invalid("backdateReason", $"Data com mais de {limits.BackdateDays} dias para trás: informe o motivo.")
            : null;
    }

    private async Task<bool> BackdatedAsync(DateOnly date, CancellationToken ct) => date < Today.AddDays(-(await parameters.LimitsAsync(ct)).BackdateDays);

    private async Task NotifyOwnersAsync(string title, string link, CancellationToken ct)
    {
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, NotificationType, title, link);
        }
    }

    private async Task<string> WhoAsync(CancellationToken ct) =>
        currentUser.UserId is { } me ? await users.DisplayNameAsync(me, ct) ?? "um usuário" : "o sistema";

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Error AlreadyReversed() => Error.Conflict("already_reversed", "Este movimento já foi estornado.");
}
