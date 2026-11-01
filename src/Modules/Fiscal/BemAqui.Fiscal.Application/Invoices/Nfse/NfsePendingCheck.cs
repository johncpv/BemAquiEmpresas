using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using Microsoft.EntityFrameworkCore.Storage;

namespace BemAqui.Fiscal.Application.Invoices.Nfse;

/// <summary>O que a conferência fez com uma NFS-e: se conferiu, se mudou, se guardou o XML, se é para gravar e a falha do Emissor.</summary>
public sealed record NfseCheckOutcome(bool Checked, bool Changed, bool Xml, bool Save, string? FailureCode = null);

/// <summary>
/// NFS-e na conferência de pendentes (FIS-06 CA 4, FIS-01 CA 14): enquanto o Emissor não tem webhook de NFS-e, a nota em processamento passa
/// pela consulta forçada (<c>POST /api/v1/nfse/{id}/consultar</c>) e recebe a mesma transição da emissão; a autorizada que ficou sem XML ou sem o
/// evento é completada. A registrada do portal não passa por aqui. Nunca reemite: sem resposta (sem documento no Emissor), só a mesma chave e o
/// mesmo payload. A nota já vem travada (<c>FOR UPDATE SKIP LOCKED</c>) pela conferência.
/// </summary>
public sealed class NfsePendingCheck(NfseReconciler reconciler, NfsePayloads payloads, NfseSubmissionService submissions, IFiscalDb db, FiscalAlerts alerts)
{
    public async Task<NfseCheckOutcome> CheckAsync(OutgoingInvoice invoice, IFiscalEmitter emitter, IDbContextTransaction transaction, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(transaction);
        if (invoice.Status == OutgoingInvoiceStatus.Authorized)
        {
            if (invoice.Origin == InvoiceOrigin.PortalRegistered)
            {
                return new NfseCheckOutcome(false, false, false, false);
            }

            var stored = await reconciler.CompleteAuthorizedAsync(invoice, emitter, cancellationToken);
            return new NfseCheckOutcome(true, false, stored, true);
        }

        if (!invoice.IsPending)
        {
            return new NfseCheckOutcome(false, false, false, false);
        }

        if (invoice.EmitterDocumentId is not { } documentId)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            var payload = await payloads.BuildAsync(invoice.Id, cancellationToken);
            if (payload is null)
            {
                await alerts.RaiseAsync(FiscalAlertReason.Unreachable, invoice.Id, cancellationToken);
                return new NfseCheckOutcome(true, false, false, false);
            }

            db.ChangeTracker.Clear();
            var resent = await submissions.SubmitAsync(invoice.Id, payload, cancellationToken);
            return new NfseCheckOutcome(true, resent.IsSuccess && resent.Value.Status != OutgoingInvoiceStatus.Processing, false, false);
        }

        var remote = await emitter.ForceConsultNfseAsync(documentId, cancellationToken);
        if (remote.IsFailure)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new NfseCheckOutcome(true, false, false, false, remote.Error!.Code);
        }

        var before = invoice.Status;
        var applied = await reconciler.ApplyAsync(invoice, remote.Value, emitter, cancellationToken);
        return new NfseCheckOutcome(true, applied && invoice.Status != before, invoice.XmlAttachmentId is not null && before != OutgoingInvoiceStatus.Authorized, true);
    }
}
