using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Commercial.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

public sealed partial class StageTemplateService
{
    public const string ItemIndex = "ux_stage_template_items_item";

    private const string OnlyServices = "Só itens de serviço podem ter modelo de etapas.";

    /// <summary>Itens de serviço vinculados ao modelo (aba Itens), resolvidos pelo <see cref="IItems"/>.</summary>
    public async Task<Result<IReadOnlyList<StageTemplateItemView>>> ListItemsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await db.StageTemplates.AnyAsync(t => t.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var links = await db.StageTemplateItems.AsNoTracking().Where(i => i.TemplateId == id).ToListAsync(cancellationToken);
        var found = (await items.FindAsync(links.Select(l => l.ItemId).ToList(), cancellationToken)).ToDictionary(i => i.Id);
        return links.Select(l => found.TryGetValue(l.ItemId, out var item)
                ? new StageTemplateItemView(l.Id, l.ItemId, item.Code, item.Name, item.IsActive && !item.IsDeleted, l.CreatedAt)
                : new StageTemplateItemView(l.Id, l.ItemId, "—", "Item não encontrado", false, l.CreatedAt))
            .OrderBy(v => v.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>Itens de serviço ativos para vincular, com o modelo que cada um já usa (um modelo por item).</summary>
    public async Task<IReadOnlyList<StageItemOption>> ItemOptionsAsync(string? q, CancellationToken cancellationToken = default)
    {
        var found = (await items.SearchAsync(ItemType.Service, q, 20, cancellationToken)).Where(i => i.IsActive && !i.IsDeleted).ToList();
        var ids = found.Select(i => i.Id).ToList();
        var links = await (from l in db.StageTemplateItems.AsNoTracking()
                           join t in db.StageTemplates.AsNoTracking() on l.TemplateId equals t.Id
                           where ids.Contains(l.ItemId)
                           select new { l.ItemId, t.Id, t.Name }).ToDictionaryAsync(x => x.ItemId, cancellationToken);
        return found.Select(i => new StageItemOption(i.Id, i.Code, i.Name, links.GetValueOrDefault(i.Id)?.Id, links.GetValueOrDefault(i.Id)?.Name)).ToList();
    }

    /// <summary>Modelo do item para a ficha do item (CA 10), só leitura.</summary>
    public async Task<ItemStageTemplateView> ForItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var link = await (from l in db.StageTemplateItems.AsNoTracking()
                          join t in db.StageTemplates.AsNoTracking() on l.TemplateId equals t.Id
                          where l.ItemId == itemId
                          select new { t.Id, t.Name }).FirstOrDefaultAsync(cancellationToken);
        return new ItemStageTemplateView(link?.Id, link?.Name);
    }

    /// <summary>
    /// Vincula um item (CA 10): o servidor confere pelo <see cref="IItems"/> que o item existe, está ativo e é <see cref="ItemType.Service"/>
    /// (422 na linha do item); um item tem um modelo só (já vinculado a outro, 422 com o nome do modelo).
    /// </summary>
    public async Task<Result<StageTemplateItemView>> LinkItemAsync(Guid id, StageTemplateItemLinkInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!await db.StageTemplates.AnyAsync(t => t.Id == id, cancellationToken))
        {
            return NotFound();
        }

        var item = (await items.FindAsync([input.ItemId], cancellationToken)) is [var found, ..] ? found : null;
        if (item is null || !item.IsActive || item.IsDeleted || item.Type != ItemType.Service)
        {
            return StageTemplateRules.Invalid("itemId", OnlyServices);
        }

        if (await TakenAsync(id, item, cancellationToken) is { } taken)
        {
            return taken;
        }

        var link = new StageTemplateItem { TemplateId = id, ItemId = item.Id, CreatedAt = clock.GetUtcNow() };
        db.StageTemplateItems.Add(link);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex, ItemIndex))
        {
            db.Entry(link).State = EntityState.Detached;
            return await TakenAsync(id, item, cancellationToken) ?? StageTemplateRules.Invalid("itemId", "O item já usa outro modelo de etapas.");
        }

        return new StageTemplateItemView(link.Id, item.Id, item.Code, item.Name, true, link.CreatedAt);
    }

    /// <summary>Desvincula: vínculo de outro modelo responde 404.</summary>
    public async Task<Result> UnlinkItemAsync(Guid id, Guid linkId, CancellationToken cancellationToken = default)
    {
        var link = await db.StageTemplateItems.FirstOrDefaultAsync(i => i.Id == linkId && i.TemplateId == id, cancellationToken);
        if (link is null || !await db.StageTemplates.AnyAsync(t => t.Id == id, cancellationToken))
        {
            return Error.NotFound("stage_template_item_not_found", "Vínculo não encontrado neste modelo.");
        }

        db.StageTemplateItems.Remove(link);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<Error?> TakenAsync(Guid id, ItemSummary item, CancellationToken ct)
    {
        var other = await (from l in db.StageTemplateItems.AsNoTracking()
                           join t in db.StageTemplates.AsNoTracking() on l.TemplateId equals t.Id
                           where l.ItemId == item.Id
                           select new { t.Id, t.Name }).FirstOrDefaultAsync(ct);
        if (other is null)
        {
            return null;
        }

        var message = other.Id == id ? $"O item {item.Code} já usa este modelo." : $"O item {item.Code} já usa o modelo {other.Name}.";
        return Error.Validation("stage_template_item_taken", message, new Dictionary<string, string[]> { ["itemId"] = [message] }) with
        {
            Data = new Dictionary<string, object?> { ["stageTemplateId"] = other.Id, ["stageTemplateName"] = other.Name },
        };
    }
}
