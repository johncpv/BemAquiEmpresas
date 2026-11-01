using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// Cancelamento em uma etapa, "Pedir cancelamento", "Recusar pedido" e carta de correção da NF-e de saída (FIS-03b CA 1 a 9, 15 e 16).
/// Permissões e 2FA no endpoint: cancelar e recusar com <c>fiscal.approve</c> (cancelar com o 2FA de novo, D6); pedir com <c>fiscal.view</c>;
/// carta com <c>fiscal.edit</c>. O prazo é conferido aqui no momento do envio, e o Emissor confere de novo antes da SEFAZ. A nota nunca é
/// apagada: o cancelamento muda a situação e grava o evento (CA 13).
/// </summary>
public sealed partial class OutgoingInvoiceService
{
    public const string CancelledMessage = "NF-e cancelada";
    public const string CancelSentMessage = "Cancelamento enviado. Aguardando a SEFAZ…";
    public const string CorrectionMessage = "Carta de correção registrada";
    public const string CorrectionSentMessage = "Carta de correção enviada. A SEFAZ não respondeu ainda; confira na aba Eventos.";
    public const string RequestType = "fiscal.cancellation_requested";
    public const string RequestDeclinedType = "fiscal.cancellation_request_declined";

    public static string RequestLink(Guid invoiceId, Guid requestId) => OutgoingInvoiceEffects.Link(invoiceId) + "?pedido=" + requestId.ToString("D");

    // ---------- CA 1 a 4 e 8: o que a nota mostra ----------

    private async Task<InvoiceCancellationView> CancellationViewAsync(OutgoingInvoice invoice, bool canEdit, CancellationToken ct)
    {
        var current = await settings.CurrentAsync(ct);
        var deadline = invoice.Status is OutgoingInvoiceStatus.Authorized ? FiscalEventRules.Deadline(invoice.AuthorizedAt, current.CancellationWindowHours) : null;
        var within = invoice.Status == OutgoingInvoiceStatus.Authorized && FiscalEventRules.WithinWindow(deadline, clock.GetUtcNow());
        var canApprove = await permissions.HasPermissionAsync(FiscalModuleInfo.Code, "approve", ct);
        var canView = await permissions.HasPermissionAsync(FiscalModuleInfo.Code, "view", ct);
        var pending = await db.CancellationRequests.AsNoTracking().FirstOrDefaultAsync(r => r.InvoiceId == invoice.Id && r.Status == CancellationRequestStatus.Pending, ct);
        var counts = await db.FiscalEvents.AsNoTracking().Where(e => e.InvoiceId == invoice.Id)
            .GroupBy(e => 1).Select(g => new { All = g.Count(), Corrections = g.Count(e => e.Type == FiscalEventType.Correction && e.Status == FiscalEventStatus.Registered) })
            .FirstOrDefaultAsync(ct);
        var corrections = counts?.Corrections ?? 0;
        return new InvoiceCancellationView(
            deadline, within, canApprove && within, !canApprove && canView && within && pending is null, canApprove && pending is not null,
            canEdit && invoice.Status == OutgoingInvoiceStatus.Authorized && corrections < FiscalEventRules.MaxCorrections, corrections,
            Math.Max(0, FiscalEventRules.MaxCorrections - corrections), invoice.CancellationSentAt, pending is null ? null : await RequestViewAsync(pending, ct), invoice.CancelledAt,
            invoice.CancelledBy is { } by ? await users.DisplayNameAsync(by, ct) : null, invoice.CancellationReason, invoice.CancellationProtocol, invoice.CancelledDanfeAttachmentId is not null,
            counts?.All ?? 0);
    }

    private async Task<CancellationRequestView> RequestViewAsync(CancellationRequest r, CancellationToken ct) =>
        new(r.Id, r.InvoiceId, r.Reason, r.Status, r.RequestedAt, r.RequestedBy is { } by ? await users.DisplayNameAsync(by, ct) : null, r.ExpiresAt, r.DecidedAt,
            r.DecidedBy is { } d ? await users.DisplayNameAsync(d, ct) : null, r.DecisionReason, r.Version);

    // ---------- CA 1, 3 e 15: cancelar ----------

    /// <summary>
    /// "Cancelar NF-e" (CA 1): motivo de 15 a 255, prazo conferido agora, "Cancelamento enviado" gravado antes do Emissor (o <c>xmin</c> deixa
    /// só um de dois pedidos juntos, CA 15) e o <c>CancelNfeAsync</c>. Homologado: <c>Cancelled</c> com o protocolo e os efeitos (evento no
    /// outbox, DANFE com a marca, avisos e e-mail). Recusado (Emissor ou SEFAZ): a nota segue autorizada e o motivo fica no evento. Sem resposta:
    /// fica "Cancelamento enviado" e o webhook <c>nfe.cancelada</c> ou a conferência confirmam pela reconsulta.
    /// </summary>
    public async Task<Result<FiscalEventResultView>> CancelAsync(Guid id, CancelInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Version != request.Version)
        {
            Clear();
            return Stale();
        }

        db.Entry(invoice).Property(i => i.Version).OriginalValue = request.Version;
        var (reason, invalid) = FiscalEventRules.Justification(request.Reason);
        if (invalid is not null)
        {
            Clear();
            return Field("reason", invalid);
        }

        CancellationRequest? asked = null;
        if (request.RequestId is { } requestId)
        {
            asked = await db.CancellationRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.InvoiceId == id, cancellationToken);
            if (asked is null)
            {
                Clear();
                return Error.NotFound(FiscalErrorCodes.NotFound, "Pedido de cancelamento não encontrado.");
            }
        }

        var current = await settings.CurrentAsync(cancellationToken);
        var now = clock.GetUtcNow();
        if (invoice.BeginCancellation(reason!, currentUser.UserId, now, FiscalEventRules.Deadline(invoice.AuthorizedAt, current.CancellationWindowHours)) is { } refused)
        {
            Clear();
            return refused;
        }

        var emitter = await access.CurrentAsync(requireActive: true, cancellationToken);
        if (emitter.IsFailure)
        {
            Clear();
            return emitter.Error!;
        }

        var sequence = await db.FiscalEvents.Where(e => e.InvoiceId == id && e.Type == FiscalEventType.Cancel).Select(e => (int?)e.Sequence).MaxAsync(cancellationToken) ?? 0;
        var evt = new FiscalEvent
        {
            InvoiceId = id, Type = FiscalEventType.Cancel, Sequence = sequence + 1, Text = reason!, RequestedBy = currentUser.UserId, RequestedAt = now, CancellationRequestId = asked?.Id,
            NotifyCustomer = request.NotifyCustomer,
        };
        db.FiscalEvents.Add(evt);
        if (await LockAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var result = await emitter.Value.CancelNfeAsync(invoice.AccessKey!, reason!, cancellationToken);
        string message;
        if (result.IsSuccess && result.Value.Accepted)
        {
            evt.Register(result.Value.Protocol, result.Value.Id, result.Value.RegisteredAt);
            invoice.MarkCancelled(result.Value.Protocol, now);
            await effects.OnStateAsync(invoice, OutgoingInvoiceStatus.Authorized, emitter.Value, cancellationToken);
            message = CancelledMessage;
        }
        else if (result.IsSuccess || NfeSubmissionService.Classify(result.Error!) == SubmissionOutcome.NotCreated)
        {
            // A SEFAZ recusou (aceito = false) ou o Emissor recusou antes dela (fora do prazo, texto): a nota continua autorizada.
            var why = result.IsSuccess ? result.Value.Reason ?? "sem motivo informado" : result.Error!.Message;
            evt.Reject(result.IsSuccess ? result.Value.Code : null, why, now);
            invoice.CancellationRefused(now);
            await db.SaveChangesAsync(cancellationToken);
            return Error.Conflict(FiscalErrorCodes.EventRejected, "A SEFAZ recusou o cancelamento: " + why);
        }
        else
        {
            message = CancelSentMessage;
        }

        await db.SaveChangesAsync(cancellationToken);
        var view = await GetAsync(id, cancellationToken);
        return new FiscalEventResultView(evt.Status, message, view.Value);
    }

    // ---------- CA 2 e 3: pedir e recusar ----------

    /// <summary>
    /// "Pedir cancelamento" (CA 2): só a autorizada dentro do prazo, um pendente por nota; avisa os membros do Proprietário com o motivo, o
    /// prazo restante e o link. Não muda a nota.
    /// </summary>
    public async Task<Result<CancellationRequestView>> RequestCancellationAsync(Guid id, CancellationRequestInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var invoice = await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Status != OutgoingInvoiceStatus.Authorized)
        {
            return Error.Conflict(FiscalErrorCodes.NotAuthorized, "Só a nota autorizada pode ter o cancelamento pedido.");
        }

        var current = await settings.CurrentAsync(cancellationToken);
        var deadline = FiscalEventRules.Deadline(invoice.AuthorizedAt, current.CancellationWindowHours);
        var now = clock.GetUtcNow();
        if (!FiscalEventRules.WithinWindow(deadline, now))
        {
            return Error.Conflict(FiscalErrorCodes.CancellationWindowClosed, FiscalEventRules.OutOfWindow);
        }

        var (reason, invalid) = FiscalEventRules.Justification(input.Reason);
        if (invalid is not null)
        {
            return Field("reason", invalid);
        }

        if (await db.CancellationRequests.AnyAsync(r => r.InvoiceId == id && r.Status == CancellationRequestStatus.Pending, cancellationToken))
        {
            return Error.Conflict(FiscalErrorCodes.RequestClosed, "Esta nota já tem um pedido de cancelamento pendente.");
        }

        var request = new CancellationRequest { InvoiceId = id, Reason = reason!, RequestedBy = currentUser.UserId, RequestedAt = now, ExpiresAt = deadline!.Value };
        db.CancellationRequests.Add(request);
        var title = $"Pedido de cancelamento da NF-e {OutgoingInvoiceEffects.Label(invoice)}: {reason}. Restam {Remaining(deadline.Value - now)}.";
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, RequestType, title, RequestLink(id, request.Id));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_cancellation_requests_pending_invoice", StringComparison.Ordinal) == true)
        {
            Clear();
            return Error.Conflict(FiscalErrorCodes.RequestClosed, "Esta nota já tem um pedido de cancelamento pendente.");
        }

        return await RequestViewAsync(request, cancellationToken);
    }

    /// <summary>"Recusar pedido" com motivo (CA 3): só o pendente desta nota (IDOR: o pedido de outra nota dá 404); avisa quem pediu.</summary>
    public async Task<Result<CancellationRequestView>> DeclineRequestAsync(Guid id, Guid requestId, DeclineCancellationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var request = await db.CancellationRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.InvoiceId == id, cancellationToken);
        var invoice = request is null ? null : await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken);
        if (request is null || invoice is null)
        {
            return Error.NotFound(FiscalErrorCodes.NotFound, "Pedido de cancelamento não encontrado.");
        }

        var (reason, invalid) = FiscalEventRules.Justification(input.Reason, "o motivo da recusa");
        if (invalid is not null)
        {
            Clear();
            return Field("reason", invalid);
        }

        if (!request.Decline(currentUser.UserId, reason!, clock.GetUtcNow()))
        {
            Clear();
            return Error.Conflict(FiscalErrorCodes.RequestClosed, "O pedido já foi decidido ou expirou.");
        }

        if (request.RequestedBy is { } requester)
        {
            notifications.Notify(requester, RequestDeclinedType, $"Pedido de cancelamento da NF-e {OutgoingInvoiceEffects.Label(invoice)} recusado: {reason}", OutgoingInvoiceEffects.Link(id));
        }

        if (await LockAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        return await RequestViewAsync(request, cancellationToken);
    }

    // ---------- CA 8, 9 e 15: carta de correção ----------

    /// <summary>
    /// "Registrar carta de correção" (CA 8): texto saneado de 15 a 1000, até 20 registradas por nota, sem cancelamento nem outra carta ao
    /// mesmo tempo (<c>xmin</c>, CA 15); registrada, o XML do evento é guardado e o e-mail <c>invoice-correction</c> vai pela fila.
    /// </summary>
    public async Task<Result<FiscalEventResultView>> CorrectAsync(Guid id, CorrectInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken);
        if (invoice is null)
        {
            return NotFound();
        }

        if (invoice.Version != request.Version)
        {
            Clear();
            return Stale();
        }

        db.Entry(invoice).Property(i => i.Version).OriginalValue = request.Version;
        var (text, invalid) = FiscalEventRules.Correction(request.Text);
        if (invalid is not null)
        {
            Clear();
            return Field("text", invalid);
        }

        var corrections = await db.FiscalEvents.Where(e => e.InvoiceId == id && e.Type == FiscalEventType.Correction).Select(e => new { e.Sequence, e.Status }).ToListAsync(cancellationToken);
        if (corrections.Count(c => c.Status == FiscalEventStatus.Registered) >= FiscalEventRules.MaxCorrections)
        {
            Clear();
            return Error.Conflict(FiscalErrorCodes.TooManyCorrections, FiscalEventRules.TooManyCorrections);
        }

        var now = clock.GetUtcNow();
        if (invoice.BeginCorrection(currentUser.UserId, now) is { } refused)
        {
            Clear();
            return refused;
        }

        var emitter = await access.CurrentAsync(requireActive: true, cancellationToken);
        if (emitter.IsFailure)
        {
            Clear();
            return emitter.Error!;
        }

        var evt = new FiscalEvent
        {
            InvoiceId = id, Type = FiscalEventType.Correction, Sequence = corrections.Select(c => c.Sequence).DefaultIfEmpty(0).Max() + 1, Text = text!, RequestedBy = currentUser.UserId,
            RequestedAt = now, NotifyCustomer = request.NotifyCustomer,
        };
        db.FiscalEvents.Add(evt);
        if (await LockAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        var result = await emitter.Value.CorrectNfeAsync(invoice.AccessKey!, text!, cancellationToken);
        invoice.CorrectionFinished();
        string message;
        if (result.IsSuccess && result.Value.Accepted)
        {
            evt.Register(result.Value.Protocol, result.Value.Id, result.Value.RegisteredAt);
            await effects.StoreEventXmlAsync(invoice, evt, invoice.AccessKey!, emitter.Value, cancellationToken);
            await effects.SendEventEmailAsync(invoice, evt, cancellationToken);
            message = CorrectionMessage;
        }
        else if (result.IsSuccess || NfeSubmissionService.Classify(result.Error!) == SubmissionOutcome.NotCreated)
        {
            var why = result.IsSuccess ? result.Value.Reason ?? "sem motivo informado" : result.Error!.Message;
            evt.Reject(result.IsSuccess ? result.Value.Code : null, why, now);
            await db.SaveChangesAsync(cancellationToken);
            return Error.Conflict(FiscalErrorCodes.EventRejected, "A SEFAZ recusou a carta de correção: " + why);
        }
        else
        {
            message = CorrectionSentMessage;
        }

        await db.SaveChangesAsync(cancellationToken);
        var view = await GetAsync(id, cancellationToken);
        return new FiscalEventResultView(evt.Status, message, view.Value);
    }

    // ---------- CA 13 e 16: eventos e arquivos ----------

    /// <summary>Aba Eventos (CA 16), do mais novo ao mais antigo.</summary>
    public async Task<Result<IReadOnlyList<FiscalEventView>>> EventsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.OutgoingInvoices.AnyAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken))
        {
            return NotFound();
        }

        var list = await db.FiscalEvents.AsNoTracking().Where(e => e.InvoiceId == id).OrderByDescending(e => e.RequestedAt).ThenByDescending(e => e.Sequence).ToListAsync(cancellationToken);
        var names = new Dictionary<Guid, string?>();
        foreach (var user in list.Where(e => e.RequestedBy != null).Select(e => e.RequestedBy!.Value).Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, cancellationToken);
        }

        return list.Select(e => EventView(e, e.RequestedBy is { } u ? names.GetValueOrDefault(u) : null)).ToList();
    }

    /// <summary>Um evento pela nota (CA 14, IDOR: o evento de outra nota dá 404).</summary>
    public async Task<Result<FiscalEventView>> EventAsync(Guid id, Guid eventId, CancellationToken cancellationToken = default)
    {
        var evt = await db.FiscalEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId && e.InvoiceId == id, cancellationToken);
        return evt is null
            ? Error.NotFound(FiscalErrorCodes.NotFound, "Evento não encontrado.")
            : EventView(evt, evt.RequestedBy is { } u ? await users.DisplayNameAsync(u, cancellationToken) : null);
    }

    private static FiscalEventView EventView(FiscalEvent e, string? requestedBy) => new(
        e.Id, e.Type, FiscalEvent.Label(e.Type), e.Sequence, e.Text, e.Status, e.Protocol, e.RequestedAt, requestedBy, e.RegisteredAt, e.RejectionCode, e.RejectionReason,
        e.XmlAttachmentId is not null, e.CancellationRequestId);

    /// <summary>XML do evento (CA 13): o anexo tem de ser da nota e do evento pedido.</summary>
    public async Task<Result<InvoiceFileContent>> EventXmlAsync(Guid id, Guid eventId, CancellationToken cancellationToken = default)
    {
        var evt = await db.FiscalEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId && e.InvoiceId == id, cancellationToken);
        return evt is null ? Error.NotFound(FiscalErrorCodes.NotFound, "Evento não encontrado.") : await AttachmentAsync(id, evt.XmlAttachmentId, "O XML do evento ainda não foi guardado.", cancellationToken);
    }

    /// <summary>DANFE da nota cancelada, com a marca (CA 6).</summary>
    public async Task<Result<InvoiceFileContent>> CancelledDanfeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.OutgoingInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.ShipmentId != null, cancellationToken);
        return invoice is null ? NotFound() : await AttachmentAsync(id, invoice.CancelledDanfeAttachmentId, "O DANFE da nota cancelada ainda não foi guardado.", cancellationToken);
    }

    private async Task<Result<InvoiceFileContent>> AttachmentAsync(Guid invoiceId, Guid? attachmentId, string missing, CancellationToken ct)
    {
        var attachment = attachmentId is { } a ? await attachments.FindAsync(a, ct) : null;
        if (attachment is null || attachment.OwnerType != OutgoingInvoiceReconciler.AttachmentOwner || attachment.OwnerId != invoiceId)
        {
            return Error.NotFound(FiscalErrorCodes.NotFound, missing);
        }

        if (attachment.ScanStatus != ScanStatus.Clean)
        {
            return Error.Conflict("attachment.not_scanned", "O arquivo ainda está sendo verificado pelo antivírus. Tente de novo em alguns minutos.");
        }

        return new InvoiceFileContent(await storage.OpenReadAsync(attachment.StoragePath, ct), attachment.ContentType, attachment.FileName);
    }

    // ---------- apoio ----------

    /// <summary>Grava a trava (situação e evento) com o <c>xmin</c>: o segundo de dois pedidos juntos recebe 409 com "Recarregar" (CA 15).</summary>
    private async Task<Error?> LockAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException || ex.InnerException?.Message.Contains("ux_fiscal_events_invoice_type_sequence", StringComparison.Ordinal) == true)
        {
            Clear();
            return Error.Conflict("concurrency_conflict", "A nota fiscal mudou desde que você abriu (outro pedido já foi enviado). Recarregue.");
        }
    }

    private static Error Stale() => Error.Conflict("concurrency_conflict", "A nota fiscal mudou desde que você abriu. Recarregue.");

    private static Error Field(string field, string message) =>
        Error.Validation(FiscalErrorCodes.Validation, message, new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>"3 h 12 min" (o mesmo texto da contagem do diálogo).</summary>
    public static string Remaining(TimeSpan left)
    {
        if (left < TimeSpan.FromMinutes(1))
        {
            return "menos de 1 min";
        }

        var minutes = (int)Math.Floor(left.TotalMinutes);
        var h = minutes / 60;
        var m = minutes % 60;
        return h > 0 ? h.ToString(CultureInfo.InvariantCulture) + " h " + m.ToString(CultureInfo.InvariantCulture) + " min" : m.ToString(CultureInfo.InvariantCulture) + " min";
    }
}
