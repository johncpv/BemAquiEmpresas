using BemAqui.BuildingBlocks.Email;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

/// <summary>
/// Guarda de envio do <c>collection-reminder</c> (FIN-09 CA 5): no momento do <c>SendEmailJob</c>, relê o título e a régua. Pago, cancelado,
/// com boleto pago aguardando conferência, com baixa de hoje, com a cobrança pausada ou com a régua desligada: o e-mail fica <c>Skipped</c>
/// com o motivo, e o lembrete também.
/// </summary>
public sealed class CollectionReminderGuard(IFinanceDb db, CollectionEligibility eligibility) : IEmailSendGuard
{
    public const string RuleOffReason = "Régua desligada";
    public const string UnknownReason = "Lembrete sem registro na régua";

    public string Template => EmailDispatchLimiter.CollectionReminderTemplate;

    public async Task<string?> SkipReasonAsync(EmailLog log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(log);
        var notice = await db.CollectionNotices.FirstOrDefaultAsync(n => n.EmailLogId == log.Id, cancellationToken);
        if (notice is null)
        {
            return UnknownReason;
        }

        var reason = await db.CollectionRules.AsNoTracking().AnyAsync(r => r.IsActive, cancellationToken)
            ? await eligibility.ReasonAsync(notice.ReceivableId, cancellationToken)
            : RuleOffReason;
        if (reason is not null)
        {
            notice.Skip(reason);
            await db.SaveChangesAsync(cancellationToken);
        }

        return reason;
    }
}
