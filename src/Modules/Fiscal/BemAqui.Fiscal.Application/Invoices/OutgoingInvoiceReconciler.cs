using System.Security.Cryptography;
using System.Text;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using Microsoft.Extensions.Logging;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>
/// Aplica na nota local o que o Emissor disse (CA 11, 13 e 14): a transição pela situação de lá (nunca pelo corpo do webhook) e, na
/// autorização, a guarda do XML como arquivo de sistema (dono <c>OutgoingInvoice</c>, prefixo do tenant no storage, SHA-256 conferido
/// na releitura, 5 anos sem expurgo). Quem chama grava (<c>SaveChanges</c>).
/// </summary>
public sealed partial class OutgoingInvoiceReconciler(
    AttachmentService attachments, IFileStorage storage, ITenantContext tenant, OutgoingInvoiceEffects effects, TimeProvider clock, ILogger<OutgoingInvoiceReconciler> logger)
{
    public const string AttachmentOwner = "OutgoingInvoice";

    /// <summary>
    /// Transição pelo que o Emissor disse e, depois, os efeitos da FIS-03a (<see cref="OutgoingInvoiceEffects"/>): DANFE, evento no outbox,
    /// e-mail, número gasto e aviso. Tudo na mesma unidade de trabalho de quem chamou.
    /// </summary>
    public async Task<bool> ApplyAsync(OutgoingInvoice invoice, NfeDocument remote, IFiscalEmitter emitter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(remote);
        ArgumentNullException.ThrowIfNull(emitter);
        var before = invoice.Status;
        var changed = invoice.Apply(remote.ToState(), clock.GetUtcNow());
        if (invoice.Status == OutgoingInvoiceStatus.Authorized && invoice.XmlAttachmentId is null && invoice.AccessKey is { } key)
        {
            changed |= await StoreXmlAsync(invoice, key, emitter, cancellationToken);
        }

        await effects.OnStateAsync(invoice, before, emitter, cancellationToken);
        return changed;
    }

    /// <summary>Autorizada que ficou sem XML, DANFE ou e-mail (o Emissor falhou na hora): a conferência completa.</summary>
    public async Task<bool> CompleteAuthorizedAsync(OutgoingInvoice invoice, IFiscalEmitter emitter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(emitter);
        var stored = false;
        if (invoice.XmlAttachmentId is null && invoice.AccessKey is { } key)
        {
            stored = await StoreXmlAsync(invoice, key, emitter, cancellationToken);
        }

        await effects.OnStateAsync(invoice, OutgoingInvoiceStatus.Authorized, emitter, cancellationToken);
        return stored;
    }

    /// <summary>Cancelada que ficou sem o evento no outbox, o DANFE com a marca ou o XML do evento (FIS-03b): a conferência completa.</summary>
    public Task CompleteCancelledAsync(OutgoingInvoice invoice, IFiscalEmitter emitter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return effects.OnStateAsync(invoice, OutgoingInvoiceStatus.Cancelled, emitter, cancellationToken);
    }

    /// <summary>Baixa e guarda o XML autorizado; falhou, fica para a próxima conferência (a nota já está autorizada).</summary>
    public async Task<bool> StoreXmlAsync(OutgoingInvoice invoice, string accessKey, IFiscalEmitter emitter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(emitter);
        var xml = await emitter.GetXmlAsync(accessKey, cancellationToken);
        if (xml.IsFailure)
        {
            var failedTenant = TenantId();
            LogXml(logger, failedTenant, invoice.Id, xml.Error!.Code);
            return false;
        }

        var content = xml.Value.Content;
        if (content.Length == 0 || !Encoding.UTF8.GetString(content).Contains(accessKey, StringComparison.Ordinal))
        {
            var emptyTenant = TenantId();
            LogXml(logger, emptyTenant, invoice.Id, "xml_without_access_key");
            return false;
        }

        var stored = await attachments.StoreGeneratedAsync(AttachmentOwner, invoice.Id, accessKey + "-nfe.xml", "application/xml", content, TenantId().ToString("N"), cancellationToken);
        await using (var reread = await storage.OpenReadAsync(stored.StoragePath, cancellationToken))
        {
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(reread, cancellationToken));
            if (!string.Equals(hash, stored.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("O XML guardado não confere com o baixado.");
            }
        }

        invoice.AttachAuthorizedXml(stored.Id, stored.Sha256);
        return true;
    }

    private Guid TenantId() => tenant.Tenant?.Id ?? Guid.Empty;

    [LoggerMessage(Level = LogLevel.Warning, Message = "EmissorFiscal: XML da nota {InvoiceId} do tenant {TenantId} não guardado ({Code}); nova tentativa na conferência.")]
    private static partial void LogXml(ILogger logger, Guid tenantId, Guid invoiceId, string code);
}
