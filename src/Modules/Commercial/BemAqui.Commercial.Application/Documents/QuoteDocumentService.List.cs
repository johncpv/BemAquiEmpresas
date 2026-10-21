using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Email;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application.Documents;

public sealed partial class QuoteDocumentService
{
    /// <summary>Aba Documentos (CA 3, 13, 14 e 15): PDFs desta revisão, envios com a situação do e-mail e anexos do usuário.</summary>
    public async Task<Result<QuoteDocuments>> DocumentsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.Quotes.AnyAsync(q => q.Id == id, cancellationToken))
        {
            return QuoteService.NotFound();
        }

        var listed = await attachments.ListAsync(OwnerType, id, cancellationToken);
        if (listed.IsFailure)
        {
            return listed.Error!;
        }

        var dispatches = await db.QuoteDispatches.AsNoTracking().Where(d => d.QuoteId == id).OrderByDescending(d => d.CreatedAt).ToListAsync(cancellationToken);
        var status = (await emailLogs.StatusAsync(dispatches.Select(d => d.EmailLogId).ToList(), cancellationToken)).ToDictionary(s => s.Id);
        var views = new List<QuoteDispatchView>();
        foreach (var d in dispatches)
        {
            var s = status.GetValueOrDefault(d.EmailLogId);
            views.Add(new QuoteDispatchView(d.Id, d.To, d.Cc, d.SentBy is { } u ? await users.DisplayNameAsync(u, cancellationToken) : null, d.CreatedAt, d.AttachmentId,
                (s?.Status ?? EmailStatus.Queued).ToString(), s?.Error));
        }

        var info = await company.GetAsync(cancellationToken);
        return new QuoteDocuments(listed.Value.Where(a => a.Origin == AttachmentOrigin.System).ToList(), listed.Value.Where(a => a.Origin == AttachmentOrigin.User).ToList(), views, info.Complete);
    }

    /// <summary>PDF atual da revisão: o mais recente gerado pelo sistema (as gerações anteriores ficam no histórico de anexos).</summary>
    private async Task<Attachment?> CurrentPdfAsync(Guid id, CancellationToken ct)
    {
        var listed = await attachments.ListAsync(OwnerType, id, ct);
        var current = listed.IsSuccess ? listed.Value.FirstOrDefault(a => a.Origin == AttachmentOrigin.System) : null;
        return current is null ? null : await attachments.FindAsync(current.Id, ct);
    }
}
