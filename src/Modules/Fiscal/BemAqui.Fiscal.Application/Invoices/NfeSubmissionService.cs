using BemAqui.Fiscal.Application.Emitter;
using BemAqui.Fiscal.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Invoices;

/// <summary>Como o envio terminou: a situação local, o desfecho e, quando houve, o erro do Emissor (campo a campo no 400).</summary>
public sealed record NfeSubmission(Guid InvoiceId, OutgoingInvoiceStatus Status, SubmissionOutcome Outcome, string IdempotencyKey, Error? EmitterError);

/// <summary>
/// Monta o pedido de NF-e de uma nota local a partir dos dados dela (FIS-03a). Até lá, nenhum: a conferência de pendentes não reenvia
/// nota que nunca recebeu resposta e só alerta.
/// </summary>
public interface IOutgoingNfePayloads
{
    Task<NfeIssueRequest?> BuildAsync(Guid invoiceId, CancellationToken cancellationToken = default);
}

public sealed class NoOutgoingNfePayloads : IOutgoingNfePayloads
{
    public Task<NfeIssueRequest?> BuildAsync(Guid invoiceId, CancellationToken cancellationToken = default) => Task.FromResult<NfeIssueRequest?>(null);
}

/// <summary>
/// Envio de uma NF-e ao Emissor (FIS-01 CA 4 a 6, ADR 0027 §4). A chave é <c>nfe-{invoiceId}-{tentativa}</c>; o hash do payload canônico
/// é gravado <b>antes</b> do POST, na entrada em <c>Processing</c>; reenvio com a mesma chave e payload diferente é recusado aqui, sem
/// chamar o Emissor (409 <c>fiscal_payload_changed</c>). Rede, tempo limite ou 5xx: a nota fica <c>Processing</c> e a próxima tentativa
/// usa a mesma chave e o mesmo payload; <c>ContingencyPending</c> nunca reemite. A FIS-03a chama este serviço da tela da nota.
/// </summary>
public sealed class NfeSubmissionService(IFiscalDb db, FiscalEmitterAccess access, OutgoingInvoiceReconciler reconciler, FiscalAlerts alerts, TimeProvider clock)
{
    public async Task<Result<NfeSubmission>> SubmitAsync(Guid invoiceId, NfeIssueRequest request, CancellationToken cancellationToken = default)
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

        var normalized = NfePayload.Normalize(request);
        if (invoice.BeginSubmission(NfePayload.Hash(normalized), clock.GetUtcNow()) is { } refused)
        {
            return refused;
        }

        // CA 5: o hash fica gravado antes do POST; se o processo cair depois, o reenvio confere o mesmo hash.
        await db.SaveChangesAsync(cancellationToken);
        var key = invoice.IdempotencyKey;
        var issued = await emitter.Value.IssueNfeAsync(normalized, key, cancellationToken);
        SubmissionOutcome outcome;
        if (issued.IsSuccess)
        {
            await reconciler.ApplyAsync(invoice, issued.Value, emitter.Value, cancellationToken);
            outcome = SubmissionOutcome.Answered;
        }
        else
        {
            outcome = Classify(issued.Error!);
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

    /// <summary>
    /// 400, 401, 403 e 429 acontecem antes de o documento existir (nenhum número gasto): a nota volta para <c>Draft</c>. Rede, tempo
    /// limite, 5xx e 409 (conflito de estado lá) deixam a dúvida: fica <c>Processing</c> e só a mesma chave tenta de novo.
    /// </summary>
    public static SubmissionOutcome Classify(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code switch
        {
            FiscalErrorCodes.Validation or FiscalErrorCodes.IntegrationMisconfigured or FiscalErrorCodes.RateLimited => SubmissionOutcome.NotCreated,
            _ => SubmissionOutcome.Uncertain,
        };
    }
}
