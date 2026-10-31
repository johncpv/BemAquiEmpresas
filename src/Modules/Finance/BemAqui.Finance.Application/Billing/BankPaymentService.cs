using System.Globalization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Finance.Application;

public enum BankPaymentOutcome
{
    /// <summary>Baixa automática gravada (CA 7).</summary>
    Settled,

    /// <summary>Entrada na conta e o boleto em <c>PaidPendingReview</c> (CA 9).</summary>
    PendingReview,

    /// <summary>O pagamento já tinha sido registrado (aviso repetido, conferência diária junto, CA 8).</summary>
    AlreadyPaid,

    Cancelled,
    Expired,

    /// <summary>A reconsulta não mostra pagamento, cancelamento nem expiração: nada a fazer.</summary>
    Unchanged,
}

public sealed record BankPaymentResult(BankPaymentOutcome Outcome, string? Reason = null);

/// <summary>De onde veio a reconsulta: o aviso (com o id do evento) ou a conferência diária.</summary>
public sealed record BankPaymentOrigin(Guid? WebhookEventId)
{
    public static BankPaymentOrigin Reconcile { get; } = new((Guid?)null);

    public string AuditLabel => WebhookEventId is { } id ? $"{BankPaymentService.WebhookActor} · evento {id}" : BankPaymentService.ReconcileActor;
}

/// <summary>
/// Efeito de uma cobrança reconsultada no Inter sobre o boleto do BemAqui (BNK-02 CA 7 a 9, 11 e 13), o mesmo para o aviso e para a
/// conferência diária. A situação, o valor e a data vêm da reconsulta (nunca do corpo do aviso). O boleto é travado (<c>FOR UPDATE</c>) na
/// transação de quem chama e a transição <c>Issued → Paid</c> passa pela entidade com o <c>xmin</c>: o segundo processamento simultâneo
/// espera o primeiro e acha o boleto já pago. A baixa usa o <see cref="ReceivableService.SettleFromBankAsync"/> (FIN-01); com diferença,
/// a entrada vai para a conta marcada "aguardando conferência" e quem tem <c>finance.edit</c> decide (R-PRP-7). O actor da auditoria é o
/// usuário de sistema "Banco Inter (aviso de pagamento)" com o id do evento.
/// </summary>
public sealed partial class BankPaymentService(
    IFinanceDb db,
    ReceivableService receivables,
    IFinanceParameters parameters,
    BemAqui.BuildingBlocks.Authorization.IPermissionChecker permissions,
    IParties parties,
    INotificationService notifications,
    IUserDirectory users,
    ICurrentUser currentUser,
    IEventPublisher events,
    AuditOrigin audit,
    TimeProvider clock)
{
    public const string WebhookActor = "Banco Inter (aviso de pagamento)";
    public const string ReconcileActor = "Banco Inter (conferência diária)";
    public const string PaidNotification = "finance.billing_slip_paid";
    public const string ReviewNotification = "finance.billing_slip_review";
    public const string AlreadyPaid = "Boleto já baixado";
    public const string StillOpen = "Situação no banco: a receber";

    /// <summary>Acima disso, na mesma hora, os avisos de pagamento viram um resumo (CA 13).</summary>
    public const int HourlyLimit = 5;

    public const string ReviewFilterLink = "/app/financeiro/receber?filtro=a-conferir";

    public static string ReviewLink(Guid slipId) => ReviewFilterLink + "&conferir=" + slipId;

    private DateOnly Today => BusinessTime.Today(clock);

    /// <summary>Aplica a cobrança reconsultada ao boleto. Abre a transação se quem chama não abriu.</summary>
    public async Task<BankPaymentResult> ApplyAsync(Guid slipId, BankSlip remote, BankPaymentOrigin origin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(origin);
        if (db.Database.CurrentTransaction is not null)
        {
            return await ApplyLockedAsync(slipId, remote, origin, cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await ApplyLockedAsync(slipId, remote, origin, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<BankPaymentResult> ApplyLockedAsync(Guid slipId, BankSlip remote, BankPaymentOrigin origin, CancellationToken ct)
    {
        var slip = await db.BillingSlips.FromSql($"SELECT s.*, s.xmin FROM finance.billing_slips s WHERE s.id = {slipId} FOR UPDATE").SingleAsync(ct);
        if (currentUser.UserId is null)
        {
            audit.Label = origin.AuditLabel;
        }

        var result = remote.Situation switch
        {
            BankSlipSituation.Paid => await PaidAsync(slip, remote, origin, ct),
            BankSlipSituation.Cancelled => await CancelledAsync(slip, remote, ct),
            BankSlipSituation.Expired => await ExpiredAsync(slip, remote, ct),
            _ => new BankPaymentResult(BankPaymentOutcome.Unchanged, slip.Status is BillingSlipStatus.Paid or BillingSlipStatus.PaidPendingReview ? AlreadyPaid : StillOpen),
        };
        await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<BankPaymentResult> PaidAsync(BillingSlip slip, BankSlip remote, BankPaymentOrigin origin, CancellationToken ct)
    {
        if (slip.Status is BillingSlipStatus.Paid or BillingSlipStatus.PaidPendingReview)
        {
            return new BankPaymentResult(BankPaymentOutcome.AlreadyPaid, AlreadyPaid);
        }

        var receivable = await db.Receivables.FirstAsync(r => r.Id == slip.ReceivableId, ct);
        var paid = Math.Round(remote.PaidAmount ?? 0m, 2, MidpointRounding.AwayFromZero);
        var creditDate = remote.PaidOn ?? Today;
        var now = clock.GetUtcNow();
        string? divergence = slip.Status switch
        {
            BillingSlipStatus.Cancelled => "O boleto estava cancelado no sistema e aparece pago no Inter.",
            BillingSlipStatus.Expired => "O boleto estava expirado no sistema e aparece pago no Inter.",
            BillingSlipStatus.Failed => "A emissão do boleto tinha falhado no sistema e ele aparece pago no Inter.",
            _ when !receivable.IsOpen => $"O título {receivable.Number} não está em aberto.",
            _ => null,
        };
        if (divergence is null)
        {
            var split = BillingSlipSettlement.Split(BillingSlipPaymentTerms.Of(slip), receivable.Balance, paid, creditDate);
            if (split.Automatic)
            {
                var settled = await receivables.SettleFromBankAsync(receivable, new BankSettlementInput(slip.BankAccountId, creditDate, split.Principal, split.Interest, split.Fine, split.Discount,
                    slip.Id, origin.WebhookEventId, CheckDiscountLimit: false), ct);
                if (settled.IsSuccess)
                {
                    slip.MarkPaid(paid, creditDate, settled.Value.Id, origin.WebhookEventId, remote.Payload, now);
                    await NotifyPaidAsync(slip, receivable, paid, now, ct);
                    return new BankPaymentResult(BankPaymentOutcome.Settled);
                }

                divergence = "A baixa automática não passou: " + settled.Error!.Message;
            }
            else
            {
                divergence = split.Divergence;
            }
        }

        await PendingReviewAsync(slip, receivable, paid, creditDate, divergence!, origin, remote.Payload, now, ct);
        return new BankPaymentResult(BankPaymentOutcome.PendingReview, divergence);
    }

    /// <summary>
    /// Divergência (CA 9): a entrada vai para a conta (o saldo bate com o banco) com a marca "Recebido do Inter, aguardando conferência" e o
    /// vínculo ao título; o boleto fica <c>PaidPendingReview</c>; quem tem <c>finance.edit</c> recebe o link para "Conferir pagamento".
    /// </summary>
    private async Task PendingReviewAsync(BillingSlip slip, Receivable receivable, decimal paid, DateOnly creditDate, string reason, BankPaymentOrigin origin, string? payload, DateTimeOffset now, CancellationToken ct)
    {
        Guid? transactionId = null;
        if (paid > 0)
        {
            var transaction = new FinancialTransaction
            {
                BankAccountId = slip.BankAccountId, Date = creditDate > Today ? Today : creditDate, Amount = paid, Type = TransactionType.Receipt,
                Description = "Recebido do Inter, aguardando conferência · " + receivable.Number, ReceivableId = receivable.Id, BillingSlipId = slip.Id, CreatedAt = now,
            }.AwaitingReview();
            db.FinancialTransactions.Add(transaction);
            transactionId = transaction.Id;
        }

        slip.MarkPendingReview(paid, creditDate, transactionId, reason, origin.WebhookEventId, payload, now);
        if (receivable.BillingSlipId is null && receivable.IsOpen)
        {
            receivable.BillingSlipId = slip.Id;
        }

        foreach (var user in await users.UsersWithPermissionAsync("finance.edit", ct))
        {
            notifications.Notify(user, ReviewNotification, $"Pagamento do título {receivable.Number} recebido do Inter com diferença: confira.", ReviewLink(slip.Id));
        }
    }

    /// <summary>
    /// "Recebido R$ {valor} de {cliente} · título {número}" a quem tem <c>finance.view</c>, uma vez por boleto (CA 13); acima de 5 na mesma
    /// hora, um resumo por hora com o link para a lista filtrada.
    /// </summary>
    private async Task NotifyPaidAsync(BillingSlip slip, Receivable receivable, decimal paid, DateTimeOffset now, CancellationToken ct)
    {
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        var thisHour = db.BillingSlips.AsNoTracking().Where(s => s.Status == BillingSlipStatus.Paid && s.ReviewDecision == null && s.PaidAt >= hour && s.Id != slip.Id);
        var count = await thisHour.CountAsync(ct) + 1;
        var recipients = await users.UsersWithPermissionAsync("finance.view", ct);
        if (count <= HourlyLimit)
        {
            var party = await parties.FindAsync(receivable.PartyId, ct);
            var title = $"Recebido {ReceivableService.Money(paid)} de {party?.TradeName ?? party?.LegalName ?? "cliente"} · título {receivable.Number}";
            foreach (var user in recipients)
            {
                await notifications.NotifyOnceAsync(user, PaidNotification, title, "/app/financeiro/receber/" + receivable.Id, "slip-paid:" + slip.Id.ToString("N"), ct);
            }

            return;
        }

        var total = await thisHour.SumAsync(s => s.PaidAmount ?? 0, ct) + paid;
        var summary = $"{count.ToString(CultureInfo.InvariantCulture)} boletos pagos nesta hora · {ReceivableService.Money(total)}";
        var key = "slip-paid-hour:" + BusinessTime.ToBusiness(hour).ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
        foreach (var user in recipients)
        {
            await notifications.NotifyOnceAsync(user, PaidNotification, summary, "/app/financeiro/receber?situacao=pago", key, ct);
        }
    }

    /// <summary>Cancelada no banco (fora do sistema ou confirmação atrasada): o título fica sem boleto (BNK-01b CA 18).</summary>
    private Task<BankPaymentResult> CancelledAsync(BillingSlip slip, BankSlip remote, CancellationToken ct)
    {
        if (!slip.HoldsReceivable)
        {
            return Task.FromResult(new BankPaymentResult(BankPaymentOutcome.Unchanged, "Boleto " + slip.Status));
        }

        return ReleaseAsync(slip, receivable =>
        {
            slip.Cancel(clock.GetUtcNow(), remote.Payload);
            events.Publish(new BillingSlipCancelled(slip.Id, receivable.Id, (slip.CancelReason ?? BillingSlipCancelReason.IssueError).ToString(), Expired: false));
            return new BankPaymentResult(BankPaymentOutcome.Cancelled);
        }, null, ct);
    }

    /// <summary>Expirada no banco (CA 18 da BNK-01b): o título continua em aberto, vencido, com "Emitir novo boleto".</summary>
    private Task<BankPaymentResult> ExpiredAsync(BillingSlip slip, BankSlip remote, CancellationToken ct)
    {
        if (slip.Status != BillingSlipStatus.Issued)
        {
            return Task.FromResult(new BankPaymentResult(BankPaymentOutcome.Unchanged, "Boleto " + slip.Status));
        }

        return ReleaseAsync(slip, receivable =>
        {
            slip.Expire(clock.GetUtcNow(), remote.Payload);
            events.Publish(new BillingSlipCancelled(slip.Id, receivable.Id, "Expired", Expired: true));
            return new BankPaymentResult(BankPaymentOutcome.Expired);
        }, receivable => $"O boleto do título {receivable.Number} expirou no Inter. Emita um novo boleto se precisar.", ct);
    }

    private async Task<BankPaymentResult> ReleaseAsync(BillingSlip slip, Func<Receivable, BankPaymentResult> apply, Func<Receivable, string>? notice, CancellationToken ct)
    {
        var receivable = await db.Receivables.FirstAsync(r => r.Id == slip.ReceivableId, ct);
        var result = apply(receivable);
        if (receivable.BillingSlipId == slip.Id)
        {
            receivable.BillingSlipId = null;
        }

        if (notice is not null)
        {
            foreach (var user in await users.UsersWithPermissionAsync("finance.edit", ct))
            {
                notifications.Notify(user, BillingSlipService.NotificationType, notice(receivable), BillingSlipService.Link(receivable.Id));
            }
        }

        return result;
    }
}
