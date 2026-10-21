using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Engineering.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Engineering.Application;

public sealed partial class RoutingTemplateService
{
    public const string ItemIndex = "ux_routing_template_items_item";

    /// <summary>Produtos vinculados ao roteiro (aba Produtos), resolvidos pelo <see cref="IItems"/>.</summary>
    public async Task<Result<IReadOnlyList<RoutingItemView>>> ListItemsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.RoutingTemplates.AnyAsync(t => t.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var links = await db.RoutingTemplateItems.AsNoTracking().Where(i => i.TemplateId == id).ToListAsync(cancellationToken);
        var found = (await items.FindAsync(links.Select(l => l.ItemId).ToList(), cancellationToken)).ToDictionary(i => i.Id);
        return links.Select(l => found.TryGetValue(l.ItemId, out var item)
                ? new RoutingItemView(l.Id, l.ItemId, item.Code, item.Name, item.Type, item.IsActive && !item.IsDeleted, l.CreatedAt)
                : new RoutingItemView(l.Id, l.ItemId, "—", "Item não encontrado", ItemType.Finished, false, l.CreatedAt))
            .OrderBy(v => v.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Vincula um produto acabado ou componente ativo (CA 7): um produto tem um roteiro só; já vinculado a outro, 422 com o
    /// nome do roteiro e o id para "Abrir roteiro".
    /// </summary>
    public async Task<Result<RoutingItemView>> LinkItemAsync(Guid id, RoutingItemLinkInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await db.RoutingTemplates.AnyAsync(t => t.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var item = (await items.FindAsync([input.ItemId], cancellationToken)) is [var found, ..] ? found : null;
        if (item is null || !item.IsActive || item.IsDeleted || item.Type is not (ItemType.Finished or ItemType.Component))
        {
            return RoutingRules.Invalid("itemId", "Escolha um produto acabado ou componente ativo.");
        }

        if (await TakenAsync(id, item, cancellationToken) is { } taken)
        {
            return taken;
        }

        var link = new RoutingTemplateItem { TemplateId = id, ItemId = item.Id, CreatedAt = clock.GetUtcNow() };
        db.RoutingTemplateItems.Add(link);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, ItemIndex))
        {
            db.Entry(link).State = EntityState.Detached;
            return await TakenAsync(id, item, cancellationToken) ?? RoutingRules.Invalid("itemId", "O produto já usa outro roteiro.");
        }

        return new RoutingItemView(link.Id, item.Id, item.Code, item.Name, item.Type, true, link.CreatedAt);
    }

    /// <summary>Desvincula (CA 16): vínculo de outro roteiro responde 404.</summary>
    public async Task<Result> UnlinkItemAsync(Guid id, Guid linkId, CancellationToken cancellationToken = default)
    {
        var link = await db.RoutingTemplateItems.FirstOrDefaultAsync(i => i.Id == linkId && i.TemplateId == id, cancellationToken);
        if (link is null || !await db.RoutingTemplates.AnyAsync(t => t.Id == id, cancellationToken))
        {
            return Error.NotFound("routing_item_not_found", "Vínculo não encontrado neste roteiro.");
        }

        db.RoutingTemplateItems.Remove(link);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> TakenAsync(Guid id, ItemSummary item, CancellationToken ct)
    {
        var other = await (from l in db.RoutingTemplateItems.AsNoTracking()
                           join t in db.RoutingTemplates.AsNoTracking() on l.TemplateId equals t.Id
                           where l.ItemId == item.Id
                           select new { t.Id, t.Name }).FirstOrDefaultAsync(ct);
        if (other is null)
        {
            return null;
        }

        var message = other.Id == id ? $"O produto {item.Code} já usa este roteiro." : $"O produto {item.Code} já usa o roteiro {other.Name}.";
        return Error.Validation("routing_item_taken", message, new Dictionary<string, string[]> { ["itemId"] = [message] }) with
        {
            Data = new Dictionary<string, object?> { ["routingTemplateId"] = other.Id, ["routingTemplateName"] = other.Name },
        };
    }
}