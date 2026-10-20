using BemAqui.Commercial.Domain;
using BemAqui.Parties.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class QuoteService
{
    private const int MaxLines = 100;

    /// <summary>
    /// Composição do item (CA 8 a 11): substitui as linhas; o custo-hora vem sempre do cargo (nunca do funcionário, CA 10);
    /// o custo unitário do item é a soma das linhas e, sem preço digitado, o preço sai do % do modo.
    /// </summary>
    public async Task<Result<QuoteCostDetail>> SetCostsAsync(Guid id, Guid itemId, QuoteCostsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await CanViewCostAsync(cancellationToken))
        {
            return NoCostAccess();
        }

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

        if (input.Lines.Count > MaxLines)
        {
            return Invalid("lines", $"A composição aceita até {MaxLines} linhas.");
        }

        if (input.Pct is < -99 or > 1000)
        {
            return Invalid("pct", "Informe um percentual entre -99% e 1.000%.");
        }

        var lines = new List<QuoteItemCost>();
        for (var i = 0; i < input.Lines.Count; i++)
        {
            var built = await LineAsync(i, item.Id, input.Lines[i], cancellationToken);
            if (built.IsFailure)
            {
                return built.Error!;
            }

            lines.Add(built.Value);
        }

        db.QuoteItemCosts.RemoveRange(await db.QuoteItemCosts.Where(c => c.QuoteItemId == item.Id).ToListAsync(cancellationToken));
        db.QuoteItemCosts.AddRange(lines);
        item.UnitCost = lines.Sum(l => l.Total);
        item.Pct = input.Pct;
        if (!item.PriceOverridden && await SuggestAsync(quote, item, input.Pct, cancellationToken) is { } suggestError)
        {
            return suggestError;
        }

        await RecalculateAsync(quote, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return await CostDetailAsync(quote, cancellationToken);
    }

    private async Task<Result<QuoteItemCost>> LineAsync(int i, Guid itemId, QuoteCostLineInput line, CancellationToken ct)
    {
        var field = $"lines[{i}]";
        var description = string.IsNullOrWhiteSpace(line.Description) ? null : line.Description.Trim();
        if (description is { Length: > 200 })
        {
            return Invalid(field + ".description", "Escreva a descrição com até 200 caracteres.");
        }

        if (line.UnitCost is < 0)
        {
            return Invalid(field + ".unitCost", "O custo não pode ser negativo.");
        }

        switch (line.CostType)
        {
            case CostType.Material:
                if (line.ItemId is not { } material || First(await items.FindAsync([material], ct)) is not { IsActive: true })
                {
                    return Invalid(field + ".itemId", "Escolha um item ativo do estoque.");
                }

                if (line.Quantity <= 0)
                {
                    return Invalid(field + ".quantity", "Informe a quantidade.");
                }

                var unit = line.UnitCost ?? await LastMaterialCostAsync(material, ct) ?? 0;
                return Build(itemId, line, description, line.Quantity, Math.Round(unit, 6, MidpointRounding.AwayFromZero));
            case CostType.Labor:
                return await LaborAsync(field, itemId, line, description, ct);
            default:
                if (line.CostType == CostType.Other && description is null)
                {
                    return Invalid(field + ".description", "Descreva o custo.");
                }

                if (line.PartyId is { } installer && !await parties.HasRoleAsync(installer, PartyRoleKind.Installer, ct))
                {
                    return Invalid(field + ".partyId", "Escolha um terceiro com o papel Instalador.");
                }

                return Build(itemId, line, description, 1, QuotePricing.Money(line.UnitCost ?? 0));
        }
    }

    private async Task<Result<QuoteItemCost>> LaborAsync(string field, Guid itemId, QuoteCostLineInput line, string? description, CancellationToken ct)
    {
        if (line.Quantity <= 0)
        {
            return Invalid(field + ".quantity", "Informe as horas.");
        }

        var positionId = line.PositionId;
        if (positionId is null && line.WorkCenterId is { } center)
        {
            var candidates = (await positions.ActiveAsync(ct)).Where(p => p.DefaultWorkCenterId == center).ToList();
            positionId = candidates.Count == 1 ? candidates[0].Id : null;
        }

        if (positionId is not { } position || First(await positions.FindAsync([position], ct)) is not { IsActive: true })
        {
            return Invalid(field + ".positionId", "Escolha um cargo ativo.");
        }

        if (line.WorkCenterId is { } wc && First(await workCenters.FindAsync([wc], ct)) is not { IsActive: true })
        {
            return Invalid(field + ".workCenterId", "Escolha um centro de trabalho ativo.");
        }

        var cost = await hourlyCost.ForPositionAsync(position, ct) ?? 0;
        return Build(itemId, line with { PositionId = position }, description, line.Quantity, Math.Round(cost, 6, MidpointRounding.AwayFromZero));
    }

    private static QuoteItemCost Build(Guid itemId, QuoteCostLineInput line, string? description, decimal quantity, decimal unitCost)
    {
        var qty = Math.Round(quantity, 4, MidpointRounding.AwayFromZero);
        return new QuoteItemCost
        {
            QuoteItemId = itemId, CostType = line.CostType, Description = description, ItemId = line.CostType == CostType.Material ? line.ItemId : null,
            WorkCenterId = line.CostType == CostType.Labor ? line.WorkCenterId : null, PositionId = line.CostType == CostType.Labor ? line.PositionId : null,
            PartyId = line.CostType == CostType.Installation ? line.PartyId : null, Quantity = qty, UnitCost = unitCost, Total = QuotePricing.LineTotal(qty, unitCost),
        };
    }

    /// <summary>Último custo unitário usado para o item em outro orçamento do tenant (CA 11); nulo se nunca usado.</summary>
    public async Task<decimal?> LastMaterialCostAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        await (from c in db.QuoteItemCosts.AsNoTracking()
               join i in db.QuoteItems.AsNoTracking() on c.QuoteItemId equals i.Id
               join q in db.Quotes.AsNoTracking() on i.QuoteId equals q.Id
               where c.CostType == CostType.Material && c.ItemId == itemId
               orderby q.CreatedAt descending
               select (decimal?)c.UnitCost).FirstOrDefaultAsync(cancellationToken);
}
