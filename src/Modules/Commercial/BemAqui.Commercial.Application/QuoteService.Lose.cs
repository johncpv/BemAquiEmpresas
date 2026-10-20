using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteService
{
    /// <summary>Registrar perda (CA 11): só o enviado; motivo obrigatório e detalhe obrigatório em "Outro".</summary>
    public async Task<Result<QuoteDetail>> LoseAsync(Guid id, QuoteLoseInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var quote = await db.Quotes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        if (quote.Status != QuoteStatus.Sent)
        {
            return Error.Conflict("quote_not_sent", quote.Status == QuoteStatus.Rejected ? "Orçamento perdido não é alterado." : "Só o orçamento enviado registra perda.");
        }

        if (quote.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O orçamento mudou desde que você abriu. Recarregue.");
        }

        if (input.Reason is not { } reason)
        {
            return Invalid("reason", "Informe o motivo da perda.");
        }

        var detail = string.IsNullOrWhiteSpace(input.Detail) ? null : input.Detail.Trim();
        if (reason == QuoteLostReason.Other && detail is null)
        {
            return Invalid("detail", "Descreva o motivo.");
        }

        if (detail is { Length: > 500 })
        {
            return Invalid("detail", "Escreva o detalhe com até 500 caracteres.");
        }

        db.Entry(quote).Property(x => x.Version).OriginalValue = input.Version;
        quote.Status = QuoteStatus.Rejected;
        quote.LostReason = reason;
        quote.LostDetail = detail;
        quote.LostAt = clock.GetUtcNow();
        quote.LostBy = currentUser.UserId;
        events.Publish(new QuoteLost(quote.Id, reason.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }
}
