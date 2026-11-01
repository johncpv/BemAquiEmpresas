using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application.Invoices.Nfse;

/// <summary>
/// Aplica na NFS-e local o que o Emissor disse (FIS-06 CA 3 e 4): a transição pela situação de lá (a mesma regra da NF-e, sem regressão), o
/// número e o código de verificação e, na autorização, o XML e o PDF da prefeitura como arquivos de sistema (dono <c>OutgoingInvoice</c>,
/// prefixo do tenant, 5 anos). Depois, os mesmos efeitos da NF-e (<see cref="OutgoingInvoiceEffects"/>): <c>InvoiceAuthorized</c> uma vez só
/// com <c>type = NFSe</c>, e-mail ao cliente pela fila, aviso da rejeição. Quem chama grava.
/// </summary>
public sealed partial class NfseReconciler(AttachmentService attachments, ITenantContext tenant, OutgoingInvoiceEffects effects, TimeProvider clock, ILogger<NfseReconciler> logger)
{
    public async Task<bool> ApplyAsync(OutgoingInvoice invoice, NfseDocument remote, IFiscalEmitter emitter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(emitter);
        var before = invoice.Status;
        var changed = invoice.ApplyNfse(remote.ToState(), remote.VerificationCode, clock.GetUtcNow());
        if (invoice.Status == OutgoingInvoiceStatus.Authorized)
        {
            changed |= await StoreFilesAsync(invoice, emitter, cancellationToken);
        }

        await effects.OnStateAsync(invoice, before, emitter, cancellationToken);
        return changed;
    }

    /// <summary>Autorizada que ficou sem XML, PDF, evento ou e-mail (o Emissor falhou na hora): a conferência completa.</summary>
    public async Task<bool> CompleteAuthorizedAsync(OutgoingInvoice invoice, IFiscalEmitter emitter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var stored = await StoreFilesAsync(invoice, emitter, cancellationToken);
        await effects.OnStateAsync(invoice, OutgoingInvoiceStatus.Authorized, emitter, cancellationToken);
        return stored;
    }

    private async Task<bool> StoreFilesAsync(OutgoingInvoice invoice, IFiscalEmitter emitter, CancellationToken ct)
    {
        if (invoice.EmitterDocumentId is not { } documentId)
        {
            return false;
        }

        var stored = false;
        var name = "nfse-" + (invoice.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? documentId.ToString("N"));
        if (invoice.XmlAttachmentId is null)
        {
            var xml = await emitter.GetNfseXmlAsync(documentId, ct);
            if (xml.IsSuccess && xml.Value.Content.Length > 0)
            {
                var file = await attachments.StoreGeneratedAsync(OutgoingInvoiceReconciler.AttachmentOwner, invoice.Id, name + ".xml", "application/xml", xml.Value.Content, TenantId().ToString("N"), ct);
                invoice.AttachAuthorizedXml(file.Id, file.Sha256);
                stored = true;
            }
            else
            {
                var failed = TenantId();
                LogFile(logger, failed, invoice.Id, "xml", xml.IsFailure ? xml.Error!.Code : "empty");
            }
        }

        if (invoice.DanfeAttachmentId is null)
        {
            var pdf = await emitter.GetNfsePdfAsync(documentId, ct);
            if (pdf.IsSuccess && pdf.Value.Content.Length > 4 && pdf.Value.Content.AsSpan(0, 4).SequenceEqual("%PDF"u8))
            {
                var file = await attachments.StoreGeneratedAsync(OutgoingInvoiceReconciler.AttachmentOwner, invoice.Id, name + ".pdf", "application/pdf", pdf.Value.Content, TenantId().ToString("N"), ct);
                invoice.AttachDanfe(file.Id);
                stored = true;
            }
            else
            {
                var failed = TenantId();
                LogFile(logger, failed, invoice.Id, "pdf", pdf.IsFailure ? pdf.Error!.Code : "not_pdf");
            }
        }

        return stored;
    }

    private Guid TenantId() => tenant.Tenant?.Id ?? Guid.Empty;

    [LoggerMessage(Level = LogLevel.Warning, Message = "EmissorFiscal: {Kind} da NFS-e {InvoiceId} do tenant {TenantId} não guardado ({Code}); nova tentativa na conferência.")]
    private static partial void LogFile(ILogger logger, Guid tenantId, Guid invoiceId, string kind, string code);
}
