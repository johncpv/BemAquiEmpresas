using BemAqui.Commercial.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteService
{
    private const int MaxItems = 200;

    /// <summary>Adicionar item (CA 7): nasce com custo zero e composição vazia.</summary>
    public async Task<Result<QuoteDetail>> AddItemAsync(Guid id, QuoteItemInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var editable = await EditableAsync(id, input.Version, cancellationToken);
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        var quote = editable.Value;
        if (await ItemErrorAsync(input, cancellationToken) is { } error)
        {
            return error;
        }

        var count = await db.QuoteItems.CountAsync(i => i.QuoteId == quote.Id, cancellationToken);
        if (count >= MaxItems)
        {
            return Invalid("items", $"O orçamento aceita até {MaxItems} itens.");
        }

        var sequence = count == 0 ? 1 : await db.QuoteItems.Where(i => i.QuoteId == quote.Id).MaxAsync(i => i.Sequence, cancellationToken) + 1;
        var item = new QuoteItem { QuoteId = quote.Id, Sequence = sequence };
        Apply(item, input);
        db.QuoteItems.Add(item);
        await RecalculateAsync(quote, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }

    /// <summary>
    /// Alterar item (CA 3 e 14): quem não vê custo muda descrição, quantidade e preço; preço digitado vale (<c>price_overridden</c>)
    /// até "Voltar ao sugerido".
    /// </summary>
    public async Task<Result<QuoteDetail>> UpdateItemAsync(Guid id, Guid itemId, QuoteItemInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var editable = await EditableAsync(id, input.Version, cancellationToken);
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        var quote = editable.Value;
        var item = await db.QuoteItems.FirstOrDefaultAsync(i => i.Id == itemId && i.QuoteId == quote.Id, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("quote_item_not_found", "Item não encontrado neste orçamento.");
        }

        if (await ItemErrorAsync(input, cancellationToken) is { } error)
        {
            return error;
        }

        Apply(item, input);
        if (input.ResetPrice || !item.PriceOverridden)
        {
            if (await SuggestAsync(quote, item, item.Pct, cancellationToken) is { } suggestError)
            {
                return suggestError;
            }
        }

        await RecalculateAsync(quote, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }

    public async Task<Result<QuoteDetail>> RemoveItemAsync(Guid id, Guid itemId, uint version, CancellationToken cancellationToken = default)
    {
        var editable = await EditableAsync(id, version, cancellationToken);
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        var quote = editable.Value;
        var item = await db.QuoteItems.FirstOrDefaultAsync(i => i.Id == itemId && i.QuoteId == quote.Id, cancellationToken);
        if (item is null)
        {
            return Error.NotFound("quote_item_not_found", "Item não encontrado neste orçamento.");
        }

        db.QuoteItemCosts.RemoveRange(await db.QuoteItemCosts.Where(c => c.QuoteItemId == item.Id).ToListAsync(cancellationToken));
        db.QuoteItems.Remove(item);
        await RecalculateAsync(quote, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await DetailAsync(quote, cancellationToken);
    }

    private async Task<Error?> ItemErrorAsync(QuoteItemInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length > 300)
        {
            return Invalid("description", "Informe a descrição, com até 300 caracteres.");
        }

        if (input.Quantity <= 0 || input.Quantity > 1_000_000)
        {
            return Invalid("quantity", "Informe uma quantidade maior que zero.");
        }

        if (input.UnitPrice is < 0)
        {
            return Invalid("unitPrice", "O preço não pode ser negativo.");
        }

        return input.ItemId is { } itemId && First(await items.FindAsync([itemId], ct)) is not { IsActive: true, IsDeleted: false }
            ? Invalid("itemId", "Escolha um item ativo do estoque.")
            : null;
    }

    private static void Apply(QuoteItem item, QuoteItemInput input)
    {
        item.Kind = input.Kind;
        item.ItemId = input.ItemId;
        item.Description = input.Description.Trim();
        item.Quantity = Math.Round(input.Quantity, 4, MidpointRounding.AwayFromZero);
        if (input.UnitPrice is { } price && !input.ResetPrice)
        {
            item.UnitPrice = QuotePricing.Money(price);
            item.PriceOverridden = true;
        }
        else if (input.ResetPrice)
        {
            item.PriceOverridden = false;
        }
    }

    /// <summary>Preço sugerido pelo modo do orçamento e pelo % do item (ou o padrão do tenant); margem ≥ 100% é recusada.</summary>
    private async Task<Error?> SuggestAsync(Quote quote, QuoteItem item, decimal? pct, CancellationToken ct)
    {
        var effective = pct ?? await commercialParameters.DefaultPricingPctAsync(ct);
        if (effective is null)
        {
            // Sem % definido: o preço fica como está até o usuário informar (CA 12).
            return item.UnitCost > 0 && !item.PriceOverridden ? Invalid("pct", "Defina o % que a empresa costuma aplicar.") : null;
        }

        if (QuotePricing.SuggestedPrice(item.UnitCost, quote.PricingMode, effective.Value) is not { } price)
        {
            return Invalid("pct", "A margem precisa ser menor que 100%.");
        }

        item.UnitPrice = price;
        item.PriceOverridden = false;
        return null;
    }
}
