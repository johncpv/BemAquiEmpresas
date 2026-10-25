using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application.Documents;

public sealed partial class QuoteDocumentService
{
    /// <summary>Sugestões do diálogo (CA 7): o contato do orçamento (ou o de cobrança), o assunto e a mensagem padrão.</summary>
    public async Task<Result<QuoteSendDraft>> SendDraftAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return QuoteService.NotFound();
        }

        var contacts = await parties.ContactsAsync(quote.PartyId, cancellationToken);
        var to = contacts.FirstOrDefault(c => c.Id == quote.ContactId)?.Email ?? (await parties.BillingContactAsync(quote.PartyId, cancellationToken))?.Email;
        var info = await company.GetAsync(cancellationToken);
        var name = OneLine(info.TradeName ?? info.LegalName);
        return new QuoteSendDraft(to, $"Orçamento {quote.Number} rev. {quote.Revision} — {name}",
            $"Olá,\n\nSegue o orçamento {quote.Number} rev. {quote.Revision} em anexo.\n\nFicamos à disposição para qualquer dúvida.\n\n{name}",
            contacts.Where(c => !string.IsNullOrWhiteSpace(c.Email)).Select(c => c.Email!).ToList());
    }

    /// <summary>
    /// Enviar por e-mail (CA 7 a 12): endereços puros, confirmação fora dos contatos, limite diário, PDF atual (gerado na hora se
    /// não existe), fila com o anexo e a cópia e, no rascunho, a passagem a Enviado.
    /// </summary>
    public async Task<Result<QuoteDocuments>> SendAsync(Guid id, QuoteSendInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return QuoteService.NotFound();
        }

        if (await SendErrorAsync(quote, input, cancellationToken) is { } error)
        {
            return error;
        }

        var cc = Cc(input);
        // Limite diário somado com os pedidos de compra (CMP-03b CA 5): a trava do tenant vale até o commit desta transação.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await limiter.CheckAsync(currentUser.UserId, await tenantParameters.EmailDispatchMaxPerUserDayAsync(cancellationToken), cancellationToken) is { } limit)
        {
            return limit;
        }

        var pdf = await CurrentPdfAsync(quote.Id, cancellationToken);
        if (pdf is null)
        {
            var built = await BuildAsync(quote.Id, cancellationToken);
            if (built.IsFailure)
            {
                return built.Error!;
            }

            pdf = await StoreAsync(built.Value, cancellationToken);
        }

        await EnqueueAsync(quote, pdf, input.To.Trim(), cc, (input.Message ?? string.Empty).Trim(), cancellationToken);
        db.Entry(quote).Property(q => q.Version).OriginalValue = input.Version;
        if (quote.Status == QuoteStatus.Draft)
        {
            quote.Status = QuoteStatus.Sent;
            quote.SentAt = clock.GetUtcNow();
        }

        events.Publish(new QuoteSent(quote.Id, quote.Revision, clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DocumentsAsync(quote.Id, cancellationToken);
    }

    private static List<string> Cc(QuoteSendInput input) => (input.Cc ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();

    private async Task<Error?> SendErrorAsync(Quote quote, QuoteSendInput input, CancellationToken ct)
    {
        if (!quote.IsCurrent)
        {
            return QuoteService.NotCurrent();
        }

        if (quote.Status is not (QuoteStatus.Draft or QuoteStatus.Sent))
        {
            return Error.Conflict("quote_not_sendable", "Só o rascunho ou o orçamento enviado podem ser enviados por e-mail.");
        }

        if (quote.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O orçamento mudou desde que você abriu. Recarregue.");
        }

        var today = BusinessTime.Today(clock);
        if (quote.ValidUntil is { } valid && valid < today)
        {
            return QuoteService.Invalid("validUntil", "A validade venceu. Ajuste a data antes de enviar.");
        }

        var cc = Cc(input);
        if (AddressError("to", input.To) is { } toError)
        {
            return toError;
        }

        if (cc.Count > 3)
        {
            return QuoteService.Invalid("cc", "Informe até 3 endereços em cópia.");
        }

        for (var i = 0; i < cc.Count; i++)
        {
            if (AddressError($"cc[{i}]", cc[i]) is { } ccError)
            {
                return ccError;
            }
        }

        if ((input.Message ?? string.Empty).Trim().Length > 2000)
        {
            return QuoteService.Invalid("message", "Escreva a mensagem com até 2.000 caracteres.");
        }

        var known = (await parties.ContactsAsync(quote.PartyId, ct)).Where(c => c.Email != null).Select(c => c.Email!.Trim().ToLowerInvariant()).ToHashSet();
        if (!input.ConfirmRecipient && new[] { input.To.Trim() }.Concat(cc).Any(a => !known.Contains(a.ToLowerInvariant())))
        {
            var party = await parties.FindAsync(quote.PartyId, ct);
            return QuoteService.Invalid("confirmRecipient", $"Este e-mail não está nos contatos de {party?.TradeName ?? party?.LegalName}. Confirme o destinatário.");
        }

        return quote.Status == QuoteStatus.Draft && !await db.QuoteItems.AnyAsync(i => i.QuoteId == quote.Id && i.UnitPrice > 0 && i.Quantity > 0, ct)
            ? QuoteService.Invalid("items", "Inclua ao menos um item com preço.")
            : null;
    }
}
