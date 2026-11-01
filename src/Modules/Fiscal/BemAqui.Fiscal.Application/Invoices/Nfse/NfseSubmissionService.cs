using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices.Nfse;

/// <summary>
/// Envio de uma NFS-e ao Emissor (FIS-06 CA 3, ADR 0027 §4), com as mesmas garantias da NF-e (<see cref="NfeSubmissionService"/>): a chave
/// <c>nfse-{invoiceId}-{tentativa}</c>, o hash do payload gravado <b>antes</b> do POST na entrada em <c>Processing</c>, reenvio com a mesma
/// chave só com o mesmo payload, e rede, tempo limite ou 5xx deixando a nota em <c>Processing</c> (a conferência consulta, nunca reemite).
/// </summary>
public sealed class NfseSubmissionService(IFiscalDb db, FiscalEmitterAccess access, NfseReconciler reconciler, FiscalAlerts alerts, TimeProvider clock)
{
    public async Task<Result<NfeSubmission>> SubmitAsync(Guid invoiceId, NfseIssueRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoice = await db.OutgoingInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId, cancellationToken);
        if (invoice is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Nota não encontrada.");
        }

        var emitter = await access.CurrentAsync(requireActive: true, cancellationToken);
        if (emitter.IsFailure)
        {
            return emitter.Error!;
        }

        if (invoice.BeginSubmission(NfsePayloads.Hash(request), clock.GetUtcNow()) is { } refused)
        {
            return refused;
        }

        await db.SaveChangesAsync(cancellationToken);
        var key = invoice.IdempotencyKey;
        var issued = await emitter.Value.IssueNfseAsync(request, key, cancellationToken);
        SubmissionOutcome outcome;
        if (issued.IsSuccess)
        {
            await reconciler.ApplyAsync(invoice, issued.Value, emitter.Value, cancellationToken);
            outcome = SubmissionOutcome.Answered;
        }
        else
        {
            outcome = NfeSubmissionService.Classify(issued.Error!);
            if (outcome == SubmissionOutcome.NotCreated)
            {
                invoice.SubmissionNotCreated();
            }

            if (issued.Error!.Code == FiscalErrorCodes.IntegrationMisconfigured)
            {
                await alerts.RaiseAsync(FiscalAlertReason.KeyRejected, invoice.Id, cancellationToken);
            }
            else if (outcome == SubmissionOutcome.Uncertain)
            {
                await alerts.RaiseAsync(FiscalAlertReason.Unreachable, invoice.Id, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new NfeSubmission(invoice.Id, invoice.Status, outcome, key, issued.IsSuccess ? null : issued.Error);
    }
}
