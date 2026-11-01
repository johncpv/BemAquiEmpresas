using System.Globalization;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using BemAqui.Identity.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// Efeitos da mudança de situação da nota (FIS-03a CA 7, 8, 9 e 14), na mesma unidade de trabalho de quem aplicou a transição (o envio,
/// o webhook ou a conferência): autorizada guarda o DANFE em PDF (dono <c>OutgoingInvoice</c>, prefixo do tenant), grava a data da nota e,
/// uma vez só, põe o <see cref="InvoiceAuthorized"/> no outbox (sem dado pessoal) e o e-mail <c>invoice-authorized</c> na fila; em produção,
/// a primeira nota grava a data de corte fiscal. Rejeitada e denegada registram o número gasto, avisam quem emitiu e vão ao outbox. Nada de
/// estoque (D4). Quem chama grava.
/// </summary>
public sealed partial class OutgoingInvoiceEffects(
    IFiscalDb db, AttachmentService attachments, IEventPublisher events, INotificationService notifications, IEmailSender email, ICompanyIdentity company, ITenantContext tenant,
    TimeProvider clock, ILogger<OutgoingInvoiceEffects> logger)
{
    public const string EmailTemplate = "invoice-authorized";
    public const string RejectedType = "fiscal.invoice_rejected";
    public const string DeniedType = "fiscal.invoice_denied";

    public static string Link(Guid invoiceId) => "/app/fiscal/saida/" + invoiceId.ToString("D");

    /// <summary>Depois da transição (ou de cada reconsulta da autorizada, para completar o que faltou).</summary>
    public async Task OnStateAsync(OutgoingInvoice invoice, OutgoingInvoiceStatus before, IFiscalEmitter emitter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(emitter);
        switch (invoice.Status)
        {
            case OutgoingInvoiceStatus.Authorized:
                await AuthorizedAsync(invoice, emitter, cancellationToken);
                break;
            case OutgoingInvoiceStatus.Rejected when before != OutgoingInvoiceStatus.Rejected:
                await GapAsync(invoice, NumberGapReasons.Rejected, cancellationToken);
                Notify(invoice, RejectedType, $"A SEFAZ rejeitou a nota {Label(invoice)}: {invoice.RejectionReason ?? "veja o motivo"}");
                events.Publish(new InvoiceRejected(invoice.Id, invoice.Type, invoice.ProjectId, invoice.RejectionCode));
                break;
            case OutgoingInvoiceStatus.Denied when before != OutgoingInvoiceStatus.Denied:
                await GapAsync(invoice, NumberGapReasons.Denied, cancellationToken);
                Notify(invoice, DeniedType, $"A SEFAZ denegou a nota {Label(invoice)}. Fale com a contadora.");
                events.Publish(new InvoiceDenied(invoice.Id, invoice.Type, invoice.ProjectId, invoice.PartyId));
                break;
        }
    }

    private async Task AuthorizedAsync(OutgoingInvoice invoice, IFiscalEmitter emitter, CancellationToken ct)
    {
        if (invoice.DanfeAttachmentId is null && invoice.AccessKey is { } key && invoice.ShipmentId is not null)
        {
            var pdf = await emitter.GetDanfePdfAsync(key, ct);
            if (pdf.IsSuccess && pdf.Value.Content.Length > 4 && pdf.Value.Content.AsSpan(0, 4).SequenceEqual("%PDF"u8))
            {
                var stored = await attachments.StoreGeneratedAsync(OutgoingInvoiceReconciler.AttachmentOwner, invoice.Id, key + "-danfe.pdf", "application/pdf", pdf.Value.Content,
                    TenantId().ToString("N"), ct);
                invoice.AttachDanfe(stored.Id);
            }
            else
            {
                var failed = TenantId();
                LogDanfe(logger, failed, invoice.Id, pdf.IsFailure ? pdf.Error!.Code : "not_pdf");
            }
        }

        var issuedOn = DateOnly.FromDateTime(BusinessTime.ToBusiness(invoice.AuthorizedAt ?? clock.GetUtcNow()).DateTime);
        if (invoice.MarkAuthorizationPublished(issuedOn, clock.GetUtcNow()))
        {
            var applies = false;
            if (invoice.Environment == FiscalEnvironment.Production && invoice.Type == InvoiceType.NFe)
            {
                // ADR 0025 adendo: a primeira nota real em produção grava a data de corte; homologação nunca entra no custo nem na receita.
                var settings = await db.FiscalSettings.FirstOrDefaultAsync(ct);
                var cut = settings?.StartProjectTaxCutover(issuedOn) ?? issuedOn;
                applies = issuedOn >= cut;
            }

            var items = await db.OutgoingInvoiceItems.AsNoTracking().Where(i => i.InvoiceId == invoice.Id).ToListAsync(ct);
            var taxes = items.SelectMany(i => i.Taxes).Where(t => t.Code != TaxCodes.Difal).GroupBy(t => (t.Code, t.InProjectCost))
                .Select(g => new InvoiceTaxAmount(g.Key.Code, g.Sum(t => t.Amount), g.Key.InProjectCost)).OrderBy(t => Array.IndexOf(TaxCodes.Order, t.Tax)).ToList();
            events.Publish(new InvoiceAuthorized(invoice.Id, invoice.Type, invoice.ProjectId, invoice.ShipmentId, invoice.PartyId, issuedOn, invoice.TotalAmount, taxes,
                invoice.CostTaxAmount, applies));
        }

        await SendAsync(invoice, resend: false, ct);
    }

    /// <summary>
    /// E-mail ao cliente (CA 14): pela fila, só com o XML e o DANFE guardados (o <c>SendEmailJob</c> anexa depois do <c>Clean</c>), corpo só
    /// com empresa, número, série, chave, valor e data. Sem e-mail de cobrança ou com "Enviar ao cliente" desmarcado, não envia.
    /// </summary>
    public async Task<bool> SendAsync(OutgoingInvoice invoice, bool resend, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.Status != OutgoingInvoiceStatus.Authorized || invoice.XmlAttachmentId is not { } xml || invoice.DanfeAttachmentId is not { } danfe
            || (!resend && (!invoice.SendToCustomer || invoice.EmailedAt is not null)) || !EmailAddresses.IsPlain(invoice.RecipientEmail))
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
            ["amount"] = invoice.TotalAmount.ToString("C", CultureInfo.GetCultureInfo("pt-BR")),
            ["issued_on"] = invoice.IssuedOn?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        };
        var cc = (invoice.EmailCc ?? string.Empty).Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(EmailAddresses.IsPlain).Take(3).ToList();
        email.Enqueue(new EmailRequest(EmailTemplate, invoice.RecipientEmail!.Trim(), model, OutgoingInvoiceReconciler.AttachmentOwner, invoice.Id, [xml, danfe], cc, info.Email,
            $"{name} via BemAqui", invoice.IssuedBy));
        invoice.EmailedAt = clock.GetUtcNow();
        return true;
    }

    /// <summary>"NF-e 2/42" ou "sem número".</summary>
    public static string Label(OutgoingInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return invoice.Number is { } n ? $"{invoice.Series?.ToString(CultureInfo.InvariantCulture)}/{n.ToString(CultureInfo.InvariantCulture)}" : "sem número";
    }

    private async Task GapAsync(OutgoingInvoice invoice, string reason, CancellationToken ct)
    {
        if (invoice.Number is not { } number || number <= 0 || invoice.Series is not { } series)
        {
            return;
        }

        var exists = db.NumberGaps.Local.Any(g => g.Series == series && g.Number == number) || await db.NumberGaps.AnyAsync(g => g.Series == series && g.Number == number, ct);
        if (!exists)
        {
            db.NumberGaps.Add(new NumberGap { Series = series, Number = number, InvoiceId = invoice.Id, Attempt = invoice.Attempt, Reason = reason, CreatedAt = clock.GetUtcNow() });
        }
    }

    private void Notify(OutgoingInvoice invoice, string type, string title)
    {
        if (invoice.IssuedBy is { } user)
        {
            notifications.Notify(user, type, title, Link(invoice.Id));
        }
    }

    private Guid TenantId() => tenant.Tenant?.Id ?? Guid.Empty;

    [LoggerMessage(Level = LogLevel.Warning, Message = "EmissorFiscal: DANFE da nota {InvoiceId} do tenant {TenantId} não guardado ({Code}); nova tentativa na conferência.")]
    private static partial void LogDanfe(ILogger logger, Guid tenantId, Guid invoiceId, string code);
}
