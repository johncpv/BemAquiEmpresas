using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.Identity.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteService
{
    /// <summary>Novo orçamento (CA 4 e 5): cliente com o papel, contato de cobrança e endereço da obra sugeridos, número ORC.</summary>
    public async Task<Result<QuoteDetail>> CreateAsync(QuoteCreateInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (await PartyErrorAsync(input.PartyId, cancellationToken) is { } partyError)
        {
            return partyError;
        }

        var contact = input.ContactId ?? (await parties.BillingContactAsync(input.PartyId, cancellationToken))?.Id;
        var site = input.SiteAddressId ?? (await parties.DefaultAddressAsync(input.PartyId, AddressKind.Site, cancellationToken))?.Id;
        if (await LinkErrorAsync(input.PartyId, input.ContactId, input.SiteAddressId, cancellationToken) is { } linkError)
        {
            return linkError;
        }

        var mode = await tenantParameters.PricingModeAsync(cancellationToken) == PricingMode.Margin ? QuotePricingMode.Margin : QuotePricingMode.Markup;
        var quote = new Quote
        {
            PartyId = input.PartyId, ContactId = contact, SiteAddressId = site, PricingMode = mode, CreatedAt = clock.GetUtcNow(), CreatedBy = currentUser.UserId,
            ValidUntil = BusinessTime.Today(clock).AddDays(await commercialParameters.ValidityDaysAsync(cancellationToken)),
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        quote.Number = await numbers.NextAsync(DocType, cancellationToken);
        db.Quotes.Add(quote);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }

    /// <summary>Cabeçalho (CA 6): contato e obra do cliente, prazo prometido e observações.</summary>
    public async Task<Result<QuoteDetail>> UpdateHeaderAsync(Guid id, QuoteHeaderInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var editable = await EditableAsync(id, input.Version, cancellationToken);
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        var quote = editable.Value;
        if (input.DeliveryDays is < 0 or > 3650)
        {
            return Invalid("deliveryDays", "Informe o prazo em dias, de 0 a 3.650.");
        }

        if (input.Notes is { Length: > 2000 })
        {
            return Invalid("notes", "Escreva as observações com até 2.000 caracteres.");
        }

        if (await LinkErrorAsync(quote.PartyId, input.ContactId, input.SiteAddressId, cancellationToken) is { } linkError)
        {
            return linkError;
        }

        quote.ContactId = input.ContactId;
        quote.SiteAddressId = input.SiteAddressId;
        quote.DeliveryDays = input.DeliveryDays;
        quote.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }

    /// <summary>Marcar como enviado (CA 16): exige ao menos um item com preço; depois disso, só leitura (CA 17).</summary>
    public async Task<Result<QuoteDetail>> SendAsync(Guid id, VersionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var editable = await EditableAsync(id, input.Version, cancellationToken);
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        var quote = editable.Value;
        if (!await db.QuoteItems.AnyAsync(i => i.QuoteId == quote.Id && i.UnitPrice > 0 && i.Quantity > 0, cancellationToken))
        {
            return Invalid("items", "Inclua ao menos um item com preço.");
        }

        if (quote.ValidUntil is { } valid && valid < BusinessTime.Today(clock))
        {
            return Invalid("validUntil", "A validade venceu. Ajuste a data antes de enviar.");
        }

        quote.Status = QuoteStatus.Sent;
        quote.SentAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }

    /// <summary>Excluir (CA 18): só o rascunho da revisão 0, excluído logicamente; o número não volta.</summary>
    public async Task<Result> DeleteAsync(Guid id, uint version, CancellationToken cancellationToken = default)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        if (quote.Status != QuoteStatus.Draft || quote.Revision > 0)
        {
            return Error.Conflict("quote_sent", "Orçamento enviado não é excluído. Registre a perda.");
        }

        if (quote.Version != version)
        {
            return Error.Conflict("concurrency_conflict", "O orçamento mudou desde que você abriu. Recarregue.");
        }

        db.Entry(quote).Property(x => x.Version).OriginalValue = version;
        quote.MarkDeleted(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> PartyErrorAsync(Guid partyId, CancellationToken ct)
    {
        var party = await parties.FindAsync(partyId, ct);
        return party is { IsActive: true, IsDeleted: false } && party.Roles.Contains(PartyRoleKind.Customer)
            ? null
            : Invalid("partyId", "Escolha um terceiro com o papel Cliente.");
    }

    /// <summary>Contato e endereço precisam ser do cliente (CA 5).</summary>
    private async Task<Error?> LinkErrorAsync(Guid partyId, Guid? contactId, Guid? addressId, CancellationToken ct)
    {
        if (contactId is { } c && !(await parties.ContactsAsync(partyId, ct)).Any(x => x.Id == c))
        {
            return Invalid("contactId", "O contato não pertence a este cliente.");
        }

        return addressId is { } a && !(await parties.AddressesAsync(partyId, ct)).Any(x => x.Id == a)
            ? Invalid("siteAddressId", "O endereço não pertence a este cliente.")
            : null;
    }
}
