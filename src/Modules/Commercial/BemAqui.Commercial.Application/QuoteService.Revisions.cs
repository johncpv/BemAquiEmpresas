using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteService
{
    internal static Error NotCurrent() => Error.Conflict("quote_not_current", "Só a revisão mais recente pode ser alterada ou aprovada.");

    /// <summary>
    /// Nova revisão (CA 1 a 5): a corrente enviada vira Substituída (a expirada continua Expirada) e nasce a próxima revisão em
    /// rascunho, com o mesmo número, itens, composições, preços, marcos e observações, e a validade recalculada.
    /// </summary>
    public async Task<Result<QuoteDetail>> NewRevisionAsync(Guid id, VersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var previous = await db.Quotes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (previous is null)
        {
            return NotFound();
        }

        if (!previous.IsCurrent)
        {
            return NotCurrent();
        }

        if (previous.Status == QuoteStatus.Approved)
        {
            return Approved();
        }

        if (previous.Status is not (QuoteStatus.Sent or QuoteStatus.Expired))
        {
            return Error.Conflict("quote_not_revisable", previous.Status == QuoteStatus.Draft
                ? "Edite o rascunho; a revisão é para orçamento enviado."
                : "Orçamento perdido ou aprovado não ganha revisão. Copie para um novo orçamento.");
        }

        if (previous.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O orçamento mudou desde que você abriu. Recarregue.");
        }

        db.Entry(previous).Property(x => x.Version).OriginalValue = input.Version;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        previous.IsCurrent = false;
        if (previous.Status == QuoteStatus.Sent)
        {
            previous.Status = QuoteStatus.Superseded;
        }

        try
        {
            // Primeiro tira a anterior de corrente: o índice único parcial (number) WHERE is_current é conferido a cada comando.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("concurrency_conflict", "Outra pessoa já criou a revisão. Recarregue.");
        }

        var quote = new Quote
        {
            Number = previous.Number, Revision = previous.Revision + 1, ParentQuoteId = previous.Id, PartyId = previous.PartyId, ContactId = previous.ContactId,
            SiteAddressId = previous.SiteAddressId, DeliveryDays = previous.DeliveryDays, Notes = previous.Notes, PricingMode = previous.PricingMode,
            ValidUntil = BusinessTime.Today(clock).AddDays(await commercialParameters.ValidityDaysAsync(cancellationToken)), CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
        };
        db.Quotes.Add(quote);
        await CopyLinesAsync(previous, quote, null, keepPrices: true, cancellationToken);
        var milestones = await db.QuoteMilestones.AsNoTracking().Where(m => m.QuoteId == previous.Id).OrderBy(m => m.Sequence).ToListAsync(cancellationToken);
        db.QuoteMilestones.AddRange(milestones.Select(m => new QuoteMilestone
        {
            QuoteId = quote.Id, Sequence = m.Sequence, Description = m.Description, Percent = m.Percent, Amount = m.Amount, Trigger = m.Trigger, TriggerEvent = m.TriggerEvent,
            DueDate = m.DueDate, DaysAfterEvent = m.DaysAfterEvent,
        }));
        await RecalculateAsync(quote, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }

    /// <summary>Prorrogar validade (CA 6 e 7, D16): só a data da corrente enviada muda, auditada; a expirada reabre com revisão.</summary>
    public async Task<Result<QuoteDetail>> ExtendValidityAsync(Guid id, ExtendValidityInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var quote = await db.Quotes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        if (!quote.IsCurrent)
        {
            return NotCurrent();
        }

        if (quote.Status == QuoteStatus.Approved)
        {
            return Approved();
        }

        if (quote.Status == QuoteStatus.Expired)
        {
            return Error.Conflict("quote_expired", "Orçamento expirado: reabra com nova revisão.");
        }

        if (quote.Status != QuoteStatus.Sent)
        {
            return Error.Conflict("quote_not_sent", "Só o orçamento enviado é prorrogado. No rascunho, ajuste a validade nas condições.");
        }

        if (quote.Version != input.Version)
        {
            return Error.Conflict("concurrency_conflict", "O orçamento mudou desde que você abriu. Recarregue.");
        }

        var today = BusinessTime.Today(clock);
        var floor = quote.ValidUntil is { } current && current > today ? current : today.AddDays(-1);
        if (input.ValidUntil <= floor)
        {
            return Invalid("validUntil", $"A nova validade precisa ser depois de {floor:dd/MM/yyyy}.");
        }

        db.Entry(quote).Property(x => x.Version).OriginalValue = input.Version;
        quote.ValidUntil = input.ValidUntil;
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }

    /// <summary>Todas as revisões do número (CA 8), da mais recente para a primeira.</summary>
    public async Task<Result<IReadOnlyList<QuoteRevisionSummary>>> RevisionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var number = await db.Quotes.AsNoTracking().Where(x => x.Id == id).Select(x => x.Number).FirstOrDefaultAsync(cancellationToken);
        if (number is null)
        {
            return NotFound();
        }

        var rows = await db.Quotes.AsNoTracking().Where(x => x.Number == number).OrderByDescending(x => x.Revision).ToListAsync(cancellationToken);
        var list = new List<QuoteRevisionSummary>();
        foreach (var r in rows)
        {
            list.Add(new QuoteRevisionSummary(r.Id, r.Revision, r.Status, r.IsCurrent, r.CreatedAt, r.CreatedBy is { } u ? await users.DisplayNameAsync(u, cancellationToken) : null, r.TotalPrice, r.ValidUntil));
        }

        return list;
    }

    /// <summary>Uma revisão do mesmo número (IDOR, CA 14), para abrir em leitura e comparar.</summary>
    public async Task<Result<QuoteDetail>> RevisionAsync(Guid id, Guid revisionId, CancellationToken cancellationToken = default)
    {
        var number = await db.Quotes.AsNoTracking().Where(x => x.Id == id).Select(x => x.Number).FirstOrDefaultAsync(cancellationToken);
        var revision = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == revisionId, cancellationToken);
        return number is null || revision is null || revision.Number != number ? NotFound() : await DetailAsync(revision, cancellationToken);
    }
}
