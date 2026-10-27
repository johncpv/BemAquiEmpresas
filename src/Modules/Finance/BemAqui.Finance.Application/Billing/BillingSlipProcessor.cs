using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application.Inter;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Parties.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BemAqui.Finance.Application;

/// <summary>
/// Corpo dos jobs do boleto (BNK-01b CA 7, 8, 13, 16 a 18). Nada aqui muda valor, situação ou vencimento do título: só o boleto e o
/// vínculo <c>receivables.billing_slip_id</c>. Toda falha vira o boleto <c>Failed</c> com o motivo traduzido e a notificação a quem
/// pediu; a nova tentativa reusa o "seu número" e consulta o banco antes de criar outra cobrança.
/// </summary>
public sealed partial class BillingSlipProcessor(
    IFinanceDb db,
    IBankBillingProvider bank,
    BankIntegrationService integrations,
    IParties parties,
    AttachmentService attachments,
    BillingSlipMailer mailer,
    IBillingSlipJobs jobs,
    INotificationService notifications,
    IUserDirectory users,
    IEventPublisher events,
    TenantContextHolder tenant,
    IOptions<InterOptions> options,
    TimeProvider clock,
    ILogger<BillingSlipProcessor> logger)
{
    public async Task IssueAsync(Guid slipId, CancellationToken cancellationToken = default)
    {
        var slip = await db.BillingSlips.FirstOrDefaultAsync(s => s.Id == slipId, cancellationToken);
        if (slip is null || slip.Status != BillingSlipStatus.Requested || slip.CancelPending)
        {
            // Cancelamento pedido antes da emissão: o CancelBillingSlipJob decide (consulta o banco se a cobrança pode existir).
            return;
        }

        var receivable = await db.Receivables.FirstAsync(r => r.Id == slip.ReceivableId, cancellationToken);
        if (tenant.Resolved is { } resolved && !resolved.IsReleased("finance"))
        {
            await FailAsync(slip, receivable, "O módulo Financeiro não está liberado nesta versão do sistema.", BillingFailureActions.Retry, cancellationToken);
            return;
        }

        var allowed = await integrations.EffectsAllowedAsync(slip.BankAccountId, cancellationToken);
        if (allowed.IsFailure)
        {
            await FailAsync(slip, receivable, allowed.Error!.Message, BillingFailureActions.FixIntegration, cancellationToken);
            return;
        }

        var integration = allowed.Value;
        if (slip.ProviderRequestId is null && !await CreateAtBankAsync(slip, receivable, integration, cancellationToken))
        {
            return;
        }

        await PollAsync(slip, receivable, integration, cancellationToken);
    }

    /// <summary>Cria a cobrança (ou acha a da tentativa anterior pelo "seu número", CA 8). Falso quando a emissão já terminou em falha.</summary>
    private async Task<bool> CreateAtBankAsync(BillingSlip slip, Receivable receivable, BankIntegration integration, CancellationToken ct)
    {
        var requestedOn = DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(slip.RequestedAt).DateTime);
        if (slip.IssueAttempts > 0 && !await FindPreviousAsync(slip, receivable, integration, requestedOn, ct))
        {
            return false;
        }

        if (slip.ProviderRequestId is not null)
        {
            return true;
        }

        var payer = await PayerAsync(receivable.PartyId, ct);
        if (payer is null)
        {
            await FailAsync(slip, receivable, "O cadastro do cliente está incompleto (CPF/CNPJ ou endereço de cobrança).", BillingFailureActions.FixParty, ct);
            return false;
        }

        // A tentativa fica gravada antes do envio: um tempo esgotado depois de o banco criar leva à consulta na próxima vez.
        slip.RecordAttempt();
        await db.SaveChangesAsync(ct);
        var terms = new BillingSlipTerms(slip.Amount, slip.DueDate, slip.FinePct, slip.InterestMonthPct, slip.DiscountAmount, slip.DiscountUntil, slip.Message);
        var created = await bank.IssueAsync(integration, new BillingIssueRequest(slip.OurReference, terms, payer), ct);
        if (created.Ok)
        {
            slip.RecordRequestId(created.Value!);
            await db.SaveChangesAsync(ct);
            return true;
        }

        if (created.Failure!.Kind == BillingFailureKind.Duplicate)
        {
            if (!await FindPreviousAsync(slip, receivable, integration, requestedOn, ct))
            {
                return false;
            }

            if (slip.ProviderRequestId is not null)
            {
                return true;
            }

            return await FailAsync(slip, receivable, "O Inter já tem uma cobrança com este número, mas ela não foi encontrada. Tente de novo em alguns minutos.", BillingFailureActions.Retry, ct);
        }

        await FailAsync(slip, receivable, created.Failure.Message, created.Failure.Action, ct);
        return false;
    }

    private async Task<bool> FindPreviousAsync(BillingSlip slip, Receivable receivable, BankIntegration integration, DateOnly requestedOn, CancellationToken ct)
    {
        var found = await bank.FindByReferenceAsync(integration, slip.OurReference, requestedOn, ct);
        if (!found.Ok)
        {
            await FailAsync(slip, receivable, found.Failure!.Message, found.Failure.Action, ct);
            return false;
        }

        if (found.Value is { } previous)
        {
            LogFoundPrevious(logger, slip.Id);
            slip.RecordRequestId(previous.RequestId);
            await db.SaveChangesAsync(ct);
        }

        return true;
    }

    /// <summary>Consulta até ter linha digitável e Pix (padrão 6); sem confirmação, agenda nova consulta; no limite, falha.</summary>
    private async Task PollAsync(BillingSlip slip, Receivable receivable, BankIntegration integration, CancellationToken ct)
    {
        var settings = options.Value;
        for (var i = 0; i < Math.Max(1, settings.BillingPollsPerRun); i++)
        {
            if (i > 0 && settings.BillingPollDelay > TimeSpan.Zero)
            {
                await Task.Delay(settings.BillingPollDelay, ct);
            }

            var read = await bank.GetAsync(integration, slip.ProviderRequestId!, ct);
            if (!read.Ok)
            {
                if (read.Failure!.Kind != BillingFailureKind.Unavailable)
                {
                    await FailAsync(slip, receivable, read.Failure.Message, read.Failure.Action, ct);
                    return;
                }

                break;
            }

            var found = read.Value!;
            switch (found.Situation)
            {
                case BankSlipSituation.Failed:
                    await FailAsync(slip, receivable, "O Inter não conseguiu emitir o boleto. Confira o cadastro do cliente e tente de novo.", BillingFailureActions.FixParty, ct, found.Payload);
                    return;
                case BankSlipSituation.Cancelled or BankSlipSituation.Expired:
                    await FailAsync(slip, receivable, "A cobrança aparece cancelada no Inter. Cancele este boleto e emita outro.", BillingFailureActions.Retry, ct, found.Payload);
                    return;
                case BankSlipSituation.Paid or BankSlipSituation.Open when !string.IsNullOrWhiteSpace(found.Data.DigitableLine):
                    if (await CompleteAsync(slip, receivable, integration, found, ct))
                    {
                        return;
                    }

                    break;
            }
        }

        slip.RecordPoll();
        if (slip.PollAttempts >= settings.BillingMaxPolls)
        {
            await FailAsync(slip, receivable, "O Inter não confirmou a emissão. Tente de novo em alguns minutos.", BillingFailureActions.Retry, ct);
            return;
        }

        await db.SaveChangesAsync(ct);
        await jobs.SchedulePollAsync(slip.Id, settings.BillingRescheduleDelay, ct);
    }

    /// <summary>Guarda o PDF do banco e passa a <c>Issued</c> (CA 7); envia ao cliente se foi pedido (CA 13). Falso sem o PDF ainda.</summary>
    private async Task<bool> CompleteAsync(BillingSlip slip, Receivable receivable, BankIntegration integration, BankSlip found, CancellationToken ct)
    {
        var pdf = await bank.PdfAsync(integration, found.RequestId, ct);
        if (!pdf.Ok || pdf.Value is not { Length: > 0 and <= BillingSlipService.MaxPdfBytes } bytes)
        {
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var stored = await attachments.StoreGeneratedAsync(BillingSlipService.AttachmentOwner, slip.Id, $"boleto-{receivable.Number}.pdf", "application/pdf", bytes, ct);
        slip.MarkIssued(found.Data, stored.Id, found.Payload, clock.GetUtcNow());
        events.Publish(new BillingSlipIssued(slip.Id, receivable.Id, receivable.PartyId, slip.Amount, slip.DueDate));
        if (slip.RequestedBy is { } requester)
        {
            notifications.Notify(requester, BillingSlipService.NotificationType, $"Boleto do título {receivable.Number} emitido pelo Inter.", BillingSlipService.Link(receivable.Id));
        }

        if (slip.SendOnIssue && (await parties.BillingContactAsync(receivable.PartyId, ct))?.Email?.Trim() is { Length: > 0 } to
            && BemAqui.BuildingBlocks.Email.EmailAddresses.IsPlain(to))
        {
            await mailer.SendOrScheduleAsync(slip, receivable, to, [], slip.RequestedBy, ct);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    private async Task<bool> FailAsync(BillingSlip slip, Receivable receivable, string reason, string action, CancellationToken ct, string? payload = null)
    {
        slip.MarkFailed(reason, action, payload);
        if (slip.RequestedBy is { } requester)
        {
            notifications.Notify(requester, BillingSlipService.NotificationType, $"A emissão do boleto do título {receivable.Number} falhou: {reason}", BillingSlipService.Link(receivable.Id));
        }

        await db.SaveChangesAsync(ct);
        return false;
    }

    /// <summary>Pagador lido agora do Parties (nunca guardado); nulo se faltar documento ou endereço de cobrança.</summary>
    private async Task<BillingPayer?> PayerAsync(Guid partyId, CancellationToken ct)
    {
        var party = await parties.FindAsync(partyId, ct);
        var address = await parties.DefaultAddressAsync(partyId, AddressKind.Billing, ct);
        if (party?.Document is not { Length: > 0 } document || !BillingSlipService.AddressComplete(address))
        {
            return null;
        }

        var contact = await parties.BillingContactAsync(partyId, ct);
        return new BillingPayer(party.Kind, party.LegalName, BillingSlipService.Digits(document), BillingSlipService.Digits(address!.ZipCode), address.Street, address.Number, address.Complement,
            address.District, address.City, address.State, contact?.Email?.Trim());
    }

    /// <summary>
    /// Cancelamento (CA 16 e 17): sem nada no banco, só aqui; senão consulta e cancela no Inter, e o boleto fica <c>Cancelled</c> depois da
    /// confirmação. Pago no banco não se cancela (a baixa vem pela BNK-02). Recusa mantém o boleto como estava, com o motivo.
    /// </summary>
    public async Task CancelAsync(Guid slipId, CancellationToken cancellationToken = default)
    {
        var slip = await db.BillingSlips.FirstOrDefaultAsync(s => s.Id == slipId, cancellationToken);
        if (slip is null || !slip.CancelPending)
        {
            return;
        }

        var receivable = await db.Receivables.FirstAsync(r => r.Id == slip.ReceivableId, cancellationToken);
        if (!slip.MayExistAtBank)
        {
            await CancelledAsync(slip, receivable, null, cancellationToken);
            return;
        }

        var integration = await db.BankIntegrations.AsNoTracking().FirstOrDefaultAsync(i => i.BankAccountId == slip.BankAccountId, cancellationToken);
        if (integration is null || !integration.IsActive)
        {
            await CancelRefusedAsync(slip, receivable, "A integração com o Inter está desligada nesta conta. Cancele o boleto no Internet Banking do Inter.", cancellationToken);
            return;
        }

        var requestId = slip.ProviderRequestId;
        if (requestId is null)
        {
            var requestedOn = DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(slip.RequestedAt).DateTime);
            var found = await bank.FindByReferenceAsync(integration, slip.OurReference, requestedOn, cancellationToken);
            if (!found.Ok)
            {
                await CancelRefusedAsync(slip, receivable, found.Failure!.Message, cancellationToken);
                return;
            }

            if (found.Value is null)
            {
                await CancelledAsync(slip, receivable, null, cancellationToken);
                return;
            }

            slip.RecordRequestId(found.Value.RequestId);
            requestId = found.Value.RequestId;
        }

        var current = await bank.GetAsync(integration, requestId, cancellationToken);
        if (current.Ok && current.Value!.Situation == BankSlipSituation.Paid)
        {
            await CancelRefusedAsync(slip, receivable, "O boleto já foi pago no Inter: a baixa vem pelo aviso do banco.", cancellationToken);
            return;
        }

        if (current.Ok && current.Value!.Situation is BankSlipSituation.Cancelled or BankSlipSituation.Expired)
        {
            await CancelledAsync(slip, receivable, current.Value.Payload, cancellationToken);
            return;
        }

        var cancelled = await bank.CancelAsync(integration, requestId, slip.CancelReason ?? BillingSlipCancelReason.IssueError, cancellationToken);
        if (!cancelled.Ok)
        {
            await CancelRefusedAsync(slip, receivable, cancelled.Failure!.Message, cancellationToken);
            return;
        }

        await CancelledAsync(slip, receivable, current.Ok ? current.Value!.Payload : null, cancellationToken);
    }

    private async Task CancelledAsync(BillingSlip slip, Receivable receivable, string? payload, CancellationToken ct)
    {
        slip.Cancel(clock.GetUtcNow(), payload);
        Release(slip, receivable);
        var reason = slip.CancelReason ?? BillingSlipCancelReason.IssueError;
        events.Publish(new BillingSlipCancelled(slip.Id, receivable.Id, reason.ToString(), Expired: false));
        if (BillingSlipRules.IsAutomatic(reason))
        {
            foreach (var user in await users.UsersWithPermissionAsync("finance.edit", ct))
            {
                notifications.Notify(user, BillingSlipService.NotificationType,
                    $"O boleto do título {receivable.Number} foi cancelado porque {BillingSlipRules.ReasonText(reason)}. Emita outro se precisar.", BillingSlipService.Link(receivable.Id));
            }
        }
        else if (slip.CancelledBy is { } by)
        {
            notifications.Notify(by, BillingSlipService.NotificationType, $"Boleto do título {receivable.Number} cancelado no Inter.", BillingSlipService.Link(receivable.Id));
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task CancelRefusedAsync(BillingSlip slip, Receivable receivable, string reason, CancellationToken ct)
    {
        var by = slip.CancelRequestedBy;
        slip.CancelRefused(reason);
        var recipients = by is { } user ? [user] : await users.UsersWithPermissionAsync("finance.edit", ct);
        foreach (var recipient in recipients)
        {
            notifications.Notify(recipient, BillingSlipService.NotificationType, $"O cancelamento do boleto do título {receivable.Number} não foi feito: {reason}", BillingSlipService.Link(receivable.Id));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>O título fica sem boleto (histórico preservado) e pode receber outro (CA 16 e 18).</summary>
    private static void Release(BillingSlip slip, Receivable receivable)
    {
        if (receivable.BillingSlipId == slip.Id)
        {
            receivable.BillingSlipId = null;
        }
    }

    /// <summary>
    /// Reconsulta da situação no banco (CA 18; a BNK-02 chama na conferência diária): "Expirado" passa o boleto a <c>Expired</c> e o título
    /// continua em aberto (vencido), com "Emitir novo boleto"; cancelado fora do sistema vira <c>Cancelled</c>. Pago fica para a BNK-02.
    /// </summary>
    public async Task<BillingSlipStatus?> RefreshAsync(Guid slipId, CancellationToken cancellationToken = default)
    {
        var slip = await db.BillingSlips.FirstOrDefaultAsync(s => s.Id == slipId, cancellationToken);
        if (slip is not { Status: BillingSlipStatus.Issued, ProviderRequestId: { } requestId })
        {
            return slip?.Status;
        }

        var integration = await db.BankIntegrations.AsNoTracking().FirstOrDefaultAsync(i => i.BankAccountId == slip.BankAccountId && i.Status == BankIntegrationStatus.Active, cancellationToken);
        if (integration is null)
        {
            return slip.Status;
        }

        var read = await bank.GetAsync(integration, requestId, cancellationToken);
        if (!read.Ok)
        {
            return slip.Status;
        }

        var receivable = await db.Receivables.FirstAsync(r => r.Id == slip.ReceivableId, cancellationToken);
        if (read.Value!.Situation == BankSlipSituation.Expired)
        {
            slip.Expire(clock.GetUtcNow(), read.Value.Payload);
            Release(slip, receivable);
            events.Publish(new BillingSlipCancelled(slip.Id, receivable.Id, "Expired", Expired: true));
            foreach (var user in await users.UsersWithPermissionAsync("finance.edit", cancellationToken))
            {
                notifications.Notify(user, BillingSlipService.NotificationType, $"O boleto do título {receivable.Number} expirou no Inter. Emita um novo boleto se precisar.", BillingSlipService.Link(receivable.Id));
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        else if (read.Value.Situation == BankSlipSituation.Cancelled)
        {
            slip.Cancel(clock.GetUtcNow(), read.Value.Payload);
            Release(slip, receivable);
            events.Publish(new BillingSlipCancelled(slip.Id, receivable.Id, (slip.CancelReason ?? BillingSlipCancelReason.IssueError).ToString(), Expired: false));
            await db.SaveChangesAsync(cancellationToken);
        }

        return slip.Status;
    }

    /// <summary>Envio adiado pelo limite diário (CA 13): sai no dia seguinte, para o destinatário guardado.</summary>
    public async Task SendScheduledEmailAsync(Guid slipId, CancellationToken cancellationToken = default)
    {
        var slip = await db.BillingSlips.FirstOrDefaultAsync(s => s.Id == slipId, cancellationToken);
        if (slip is not { Status: BillingSlipStatus.Issued, ScheduledEmailAt: not null, ScheduledEmailTo: { Length: > 0 } to })
        {
            return;
        }

        var receivable = await db.Receivables.AsNoTracking().FirstAsync(r => r.Id == slip.ReceivableId, cancellationToken);
        var cc = slip.ScheduledEmailCc?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [];
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await mailer.EnqueueAsync(slip, receivable, to, cc, slip.RequestedBy, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Boleto {SlipId}: cobrança da tentativa anterior encontrada no banco pelo seu número.")]
    private static partial void LogFoundPrevious(ILogger logger, Guid slipId);
}
