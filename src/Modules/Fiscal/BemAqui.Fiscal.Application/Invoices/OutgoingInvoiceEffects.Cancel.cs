using System.Globalization;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// Efeitos do cancelamento (FIS-03b CA 1, 3, 5, 6 e 7), na unidade de trabalho de quem confirmou (a resposta do evento, o webhook
/// <c>nfe.cancelada</c> com reconsulta ou a conferência de pendentes) e idempotentes: o evento fica registrado com o protocolo (cancelada fora do
/// BemAqui ganha o registro), o XML do evento e o DANFE com a marca "CANCELADA" são guardados ao lado dos originais (nunca no lugar), o
/// <see cref="InvoiceCancelled"/> vai uma vez só ao outbox, os membros do Proprietário são avisados, o pedido pendente fica atendido e o
/// e-mail <c>invoice-cancelled</c> sai pela fila. Nada de estoque (D12).
/// </summary>
public sealed partial class OutgoingInvoiceEffects
{
    public const string CancelledEmailTemplate = "invoice-cancelled";
    public const string CorrectionEmailTemplate = "invoice-correction";
    public const string CancelledType = "fiscal.invoice_cancelled";
    public const string RequestDoneType = "fiscal.cancellation_request_done";
    public const string CancelledBand = "Cancelada — documento cancelado na SEFAZ";
    public const string OutsideText = "Cancelada fora do BemAqui (no Emissor Fiscal).";

    private async Task CancelledAsync(OutgoingInvoice invoice, IFiscalEmitter emitter, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cancel = db.FiscalEvents.Local.Where(e => e.InvoiceId == invoice.Id && e.Type == FiscalEventType.Cancel).OrderByDescending(e => e.Sequence).FirstOrDefault()
            ?? await db.FiscalEvents.Where(e => e.InvoiceId == invoice.Id && e.Type == FiscalEventType.Cancel).OrderByDescending(e => e.Sequence).FirstOrDefaultAsync(ct);
        if (cancel is null || cancel.Status == FiscalEventStatus.Rejected)
        {
            // Cancelada no painel do Emissor (ou depois de uma tentativa recusada): o histórico ganha o registro do que a SEFAZ homologou.
            cancel = new FiscalEvent
            {
                InvoiceId = invoice.Id, Type = FiscalEventType.Cancel, Sequence = (cancel?.Sequence ?? 0) + 1, Text = invoice.CancellationReason ?? OutsideText,
                RequestedBy = invoice.CancelledBy, RequestedAt = invoice.CancelledAt ?? now,
            };
            db.FiscalEvents.Add(cancel);
        }

        if (!cancel.IsRegistered || cancel.EmitterEventId is null)
        {
            var (protocol, emitterId) = await RegisteredEventAsync(invoice, NfeEventType.Cancellation, emitter, ct);
            cancel.Register(invoice.CancellationProtocol ?? protocol, emitterId, invoice.CancelledAt ?? now);
        }

        invoice.MarkCancelled(cancel.Protocol, now);
        if (invoice.AccessKey is { } key)
        {
            await StoreEventXmlAsync(invoice, cancel, key, emitter, ct);
            if (invoice.CancelledDanfeAttachmentId is null)
            {
                await StoreCancelledDanfeAsync(invoice, key, emitter, ct);
            }
        }

        var cancelledOn = DateOnly.FromDateTime(BusinessTime.ToBusiness(invoice.CancelledAt ?? now).DateTime);
        if (invoice.MarkCancellationPublished(cancelledOn, now))
        {
            var applies = false;
            if (invoice.Environment == FiscalEnvironment.Production && invoice.Type == InvoiceType.NFe && invoice.IssuedOn is { } issued)
            {
                // A mesma regra da autorização (ADR 0025 adendo): só a nota que entrou no custo ganha o reverso.
                var cutover = await db.FiscalSettings.AsNoTracking().Select(s => s.ProjectTaxCutoverOn).FirstOrDefaultAsync(ct);
                applies = cutover is { } cut && issued >= cut;
            }

            events.Publish(new InvoiceCancelled(invoice.Id, invoice.Type, invoice.ProjectId, invoice.ShipmentId, cancelledOn, invoice.CostTaxAmount, invoice.TotalAmount, applies));
            await NotifyCancelledAsync(invoice, cancel, ct);
            await CloseRequestsAsync(invoice, now, ct);
        }

        await SendEventEmailAsync(invoice, cancel, ct);
    }

    /// <summary>O evento homologado no Emissor (protocolo e id), para o registro feito pelo webhook ou pela conferência.</summary>
    private static async Task<(string? Protocol, Guid? Id)> RegisteredEventAsync(OutgoingInvoice invoice, NfeEventType type, IFiscalEmitter emitter, CancellationToken ct)
    {
        if (invoice.AccessKey is not { } key)
        {
            return (null, null);
        }

        var list = await emitter.GetEventsAsync(key, ct);
        var found = list.IsSuccess ? list.Value.LastOrDefault(e => e.Type == type && e.Accepted) : null;
        return (found?.Protocol, found?.Id);
    }

    /// <summary>XML do evento (lacuna L-n): guardado como anexo de sistema da nota; sem a rota no Emissor, fica para a próxima conferência.</summary>
    public async Task StoreEventXmlAsync(OutgoingInvoice invoice, FiscalEvent evt, string accessKey, IFiscalEmitter emitter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(emitter);
        if (evt.XmlAttachmentId is not null || evt.EmitterEventId is not { } eventId)
        {
            return;
        }

        var xml = await emitter.GetEventXmlAsync(accessKey, eventId, cancellationToken);
        if (xml.IsFailure || xml.Value.Content.Length == 0)
        {
            var failed = TenantId();
            LogEventXml(logger, failed, invoice.Id, xml.IsFailure ? xml.Error!.Code : "empty");
            return;
        }

        var name = accessKey + "-" + (evt.Type == FiscalEventType.Cancel ? "cancelamento" : "cce-" + evt.Sequence.ToString("00", CultureInfo.InvariantCulture)) + ".xml";
        var stored = await attachments.StoreGeneratedAsync(OutgoingInvoiceReconciler.AttachmentOwner, invoice.Id, name, "application/xml", xml.Value.Content, TenantId().ToString("N"), cancellationToken);
        evt.AttachXml(stored.Id);
    }

    /// <summary>DANFE com a marca "CANCELADA" do Emissor (lacuna L-c); sem o conversor, a reserva do BemAqui monta o PDF com a faixa.</summary>
    private async Task StoreCancelledDanfeAsync(OutgoingInvoice invoice, string key, IFiscalEmitter emitter, CancellationToken ct)
    {
        byte[]? content = null;
        var pdf = await emitter.GetDanfePdfAsync(key, ct);
        if (pdf.IsSuccess && pdf.Value.Content.Length > 4 && pdf.Value.Content.AsSpan(0, 4).SequenceEqual("%PDF"u8))
        {
            content = pdf.Value.Content;
        }
        else
        {
            var failed = TenantId();
            LogDanfe(logger, failed, invoice.Id, pdf.IsFailure ? pdf.Error!.Code : "not_pdf");
            if (invoice.Number is { } number && invoice.Series is { } series)
            {
                var info = await company.GetAsync(ct);
                var items = await db.OutgoingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == invoice.Id).OrderBy(i => i.Sequence).ToListAsync(ct);
                content = await renderer.RenderAsync(new DanfePreviewDocument(new DanfePreviewModel(info.LegalName, info.Document, key, number, series, [], invoice, items,
                    invoice.AdditionalInformation, CancelledBand)), ct);
            }
        }

        if (content is not null)
        {
            var stored = await attachments.StoreGeneratedAsync(OutgoingInvoiceReconciler.AttachmentOwner, invoice.Id, key + "-danfe-cancelada.pdf", "application/pdf", content,
                TenantId().ToString("N"), ct);
            invoice.AttachCancelledDanfe(stored.Id);
        }
    }

    /// <summary>CA 7: "NF-e {número} cancelada por {usuário}: {motivo}" para todos os membros do Proprietário, mesmo quando foi um deles.</summary>
    private async Task NotifyCancelledAsync(OutgoingInvoice invoice, FiscalEvent cancel, CancellationToken ct)
    {
        var by = invoice.CancelledBy is { } u ? await users.DisplayNameAsync(u, ct) : null;
        var title = $"NF-e {Label(invoice)} cancelada por {by ?? "fora do BemAqui"}: {cancel.Text}";
        foreach (var owner in await users.OwnerIdsAsync(ct))
        {
            notifications.Notify(owner, CancelledType, title, Link(invoice.Id));
        }
    }

    /// <summary>CA 3: o pedido pendente fica atendido e quem pediu é avisado.</summary>
    private async Task CloseRequestsAsync(OutgoingInvoice invoice, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var request in await db.CancellationRequests.Where(r => r.InvoiceId == invoice.Id && r.Status == CancellationRequestStatus.Pending).ToListAsync(ct))
        {
            if (request.Complete(invoice.CancelledBy, now) && request.RequestedBy is { } requester)
            {
                notifications.Notify(requester, RequestDoneType, $"Pedido de cancelamento atendido: a NF-e {Label(invoice)} foi cancelada.", Link(invoice.Id));
            }
        }
    }

    /// <summary>
    /// E-mail do evento ao cliente (CA 6 e 8): pela fila, com o XML do evento e, no cancelamento, o DANFE com a marca; só com o e-mail de
    /// cobrança válido e a opção marcada. Uma vez por evento.
    /// </summary>
    public async Task<bool> SendEventEmailAsync(OutgoingInvoice invoice, FiscalEvent evt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(evt);
        var cancelling = evt.Type == FiscalEventType.Cancel;
        var files = new List<Guid>();
        if (evt.XmlAttachmentId is { } xml)
        {
            files.Add(xml);
        }

        if (cancelling && invoice.CancelledDanfeAttachmentId is { } danfe)
        {
            files.Add(danfe);
        }

        if (!evt.IsRegistered || !evt.NotifyCustomer || evt.EmailedAt is not null || !EmailAddresses.IsPlain(invoice.RecipientEmail) || (cancelling && invoice.CancelledDanfeAttachmentId is null))
        {
            return false;
        }

        var info = await company.GetAsync(cancellationToken);
        var name = EmailAddresses.OneLine(info.TradeName ?? info.LegalName);
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["tenant_name"] = name,
            ["invoice_number"] = Label(invoice),
            ["number"] = invoice.Number?.ToString(CultureInfo.InvariantCulture),
            ["series"] = invoice.Series?.ToString(CultureInfo.InvariantCulture),
            ["access_key"] = invoice.AccessKey,
            ["protocol"] = evt.Protocol,
            ["sequence"] = evt.Sequence.ToString(CultureInfo.InvariantCulture),
            ["text"] = evt.Text,
            ["has_xml"] = evt.XmlAttachmentId is not null,
        };
        var cc = (invoice.EmailCc ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(EmailAddresses.IsPlain).Take(3).ToList();
        email.Enqueue(new EmailRequest(cancelling ? CancelledEmailTemplate : CorrectionEmailTemplate, invoice.RecipientEmail!.Trim(), model, OutgoingInvoiceReconciler.AttachmentOwner,
            invoice.Id, files, cc, info.Email, $"{name} via BemAqui", evt.RequestedBy));
        evt.EmailedAt = clock.GetUtcNow();
        return true;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "EmissorFiscal: XML do evento da nota {InvoiceId} do tenant {TenantId} não guardado ({Code}); nova tentativa na conferência.")]
    private static partial void LogEventXml(ILogger logger, Guid tenantId, Guid invoiceId, string code);
}
