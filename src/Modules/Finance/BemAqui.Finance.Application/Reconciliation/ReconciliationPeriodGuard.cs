using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application.Reconciliation;

/// <summary>
/// Período de conciliação fechado (BNK-03b CA 11): depois de "Fechar conciliação até dd/mm", desfazer conciliação, ignorar linha ou lançar
/// transação com data no período exige <c>finance.approve</c> e motivo, e os membros do Proprietário são avisados. O lançamento sem OP e a
/// transferência (FIN-04), a baixa e o estorno do título a receber (FIN-01), o pagamento da OP (FIN-03a), o ignorar linha (BNK-03a) e o
/// desfazer da conciliação passam por aqui antes de gravar. O aviso entra no mesmo <c>SaveChanges</c> de quem chama.
/// </summary>
public sealed class ReconciliationPeriodGuard(IFinanceDb db, IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, INotificationService notifications)
{
    public const string ClosedCode = "reconciliation_period_closed";
    public const string NotificationType = "finance.reconciliation_closed_period";

    /// <summary>Até quando a conciliação da conta está fechada (o fechamento mais recente), ou nulo.</summary>
    public async Task<DateOnly?> ClosedUntilAsync(Guid bankAccountId, CancellationToken cancellationToken = default) =>
        await db.BankReconciliationClosings.AsNoTracking().Where(c => c.BankAccountId == bankAccountId)
            .MaxAsync(c => (DateOnly?)c.UntilDate, cancellationToken);

    public async Task<bool> IsClosedAsync(Guid bankAccountId, DateOnly date, CancellationToken cancellationToken = default) =>
        await ClosedUntilAsync(bankAccountId, cancellationToken) is { } until && date <= until;

    /// <summary>
    /// Confere a data contra o fechamento da conta. Fora do período fechado, nada. Dentro: sem <c>finance.approve</c>, 403; sem motivo,
    /// 422 no campo <paramref name="reasonField"/>; com os dois, avisa o Proprietário ("{quem} {ação} … no período fechado até …").
    /// </summary>
    /// <param name="action">O que a pessoa faz, no infinitivo ("lançar", "desfazer a conciliação").</param>
    public async Task<Error?> CheckAsync(Guid bankAccountId, DateOnly date, string? reason, string reasonField, string action, CancellationToken cancellationToken = default)
    {
        if (await ClosedUntilAsync(bankAccountId, cancellationToken) is not { } until || date > until)
        {
            return null;
        }

        if (!await permissions.HasPermissionAsync("finance", StandardActions.Approve, cancellationToken))
        {
            return Error.Forbidden(ClosedCode, $"A conciliação desta conta está fechada até {until:dd/MM/yyyy}: só quem aprova em Caixa e pagamentos pode {action} com data nesse período.");
        }

        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 200)
        {
            var message = $"A conciliação está fechada até {until:dd/MM/yyyy}: informe o motivo (até 200 caracteres).";
            return Error.Validation(Error.Codes.Validation, message, new Dictionary<string, string[]> { [reasonField] = [message] });
        }

        var account = await db.BankAccounts.AsNoTracking().Where(a => a.Id == bankAccountId).Select(a => a.Name).FirstOrDefaultAsync(cancellationToken);
        var who = currentUser.UserId is { } me ? await users.DisplayNameAsync(me, cancellationToken) ?? "Um usuário" : "O sistema";
        var title = $"Período de conciliação fechado até {until:dd/MM/yyyy} na conta {account}: {who} vai {action} com data de {date:dd/MM/yyyy}. Motivo: {Cut(text, 120)}";
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, NotificationType, title, ReconciliationService.Link(bankAccountId));
        }

        return null;
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
