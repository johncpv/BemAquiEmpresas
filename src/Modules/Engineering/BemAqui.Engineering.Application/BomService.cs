using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.Engineering.Domain;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Engineering.Application;

/// <summary>
/// BOM da OS de fabricação (ENG-02): criar, copiar, salvar, liberar, revisar e descartar, com a necessidade da OS na unidade de estoque e
/// os eventos <c>BomReleased</c>/<c>BomRevised</c> no outbox. A OS, o item e a unidade são só ids, validados pelos contratos da Produção e
/// do Estoque (ADR 0006); o saldo vem do <see cref="IBomStock"/> (ligado na EST-01a).
/// </summary>
public sealed partial class BomService(
    IEngineeringDb db, IProductionOrders orders, IItems items, IItemUnits units, IUnitConversion conversion, IBomStock stock, IPermissionChecker permissions,
    ICurrentUser currentUser, IUserDirectory users, IEventPublisher events, TimeProvider clock)
{
    public const string RevisionIndex = "ux_boms_order_revision";
    public const string ReleasedIndex = "ux_boms_released";
    public const string DraftIndex = "ux_boms_draft";
    public const string LineItemIndex = "ux_bom_lines_item";

    /// <summary>Aba BOM da OS (CA 2 a 4, 15): o cabeçalho, a liberada vigente, o rascunho e as revisões.</summary>
    public async Task<Result<BomOrderView>> GetOrderAsync(Guid productionOrderId, CancellationToken cancellationToken = default)
    {
        var order = await orders.FindAsync(productionOrderId, cancellationToken);
        if (order is null)
        {
            return OrderNotFound();
        }

        var boms = await db.Boms.AsNoTracking().Where(b => b.ProductionOrderId == productionOrderId).OrderByDescending(b => b.Revision).ToListAsync(cancellationToken);
        var current = boms.Where(b => b.Status != BomStatus.Superseded).ToList();
        var details = (await DetailsAsync(current, cancellationToken)).ToDictionary(d => d.Id);
        var counts = await LineCountsAsync(boms.Select(b => b.Id).ToList(), cancellationToken);
        var names = await NamesAsync(boms.Select(b => b.ReleasedBy), cancellationToken);
        var revisions = boms.Select(b => new BomRevisionSummary(b.Id, b.Revision, b.Status, counts.GetValueOrDefault(b.Id), b.CreatedAt, b.ReleasedAt,
            b.ReleasedBy is { } by ? names.GetValueOrDefault(by) : null)).ToList();
        return new BomOrderView(await HeaderAsync(order, cancellationToken),
            current.FirstOrDefault(b => b.Status == BomStatus.Released) is { } released ? details[released.Id] : null,
            current.FirstOrDefault(b => b.Status == BomStatus.Draft) is { } draft ? details[draft.Id] : null,
            revisions);
    }

    public async Task<Result<BomDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var bom = await db.Boms.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        return bom is null ? BomRules.NotFound() : await DetailAsync(bom, cancellationToken);
    }

    /// <summary>Linha da BOM (CA 19, IDOR): linha de outra BOM responde 404.</summary>
    public async Task<Result<BomLineView>> GetLineAsync(Guid id, Guid lineId, CancellationToken cancellationToken = default)
    {
        var bom = await db.Boms.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bom is null || !await db.BomLines.AnyAsync(l => l.Id == lineId && l.BomId == id, cancellationToken))
        {
            return Error.NotFound("bom_line_not_found", "Linha não encontrada nesta BOM.");
        }

        return (await DetailAsync(bom, cancellationToken)).Lines.Single(l => l.Id == lineId);
    }

    /// <summary>
    /// Engenharia → BOMs (CA 17): as OS de fabricação em aberto com a situação da BOM; <c>will_stop</c> são as OS com a etapa atual
    /// depois da primeira (ou a primeira já concluída) e sem BOM liberada, que vão parar no próximo início de etapa (D12).
    /// </summary>
    public async Task<PagedList<BomOrderSummary>> ListAsync(BomListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var open = await orders.OpenAsync(cancellationToken);
        var ids = open.Select(o => o.Id).ToList();
        var boms = (await db.Boms.AsNoTracking().Where(b => ids.Contains(b.ProductionOrderId) && b.Status != BomStatus.Superseded).ToListAsync(cancellationToken))
            .ToLookup(b => b.ProductionOrderId);
        var counts = await LineCountsAsync(boms.SelectMany(g => g).Where(b => b.IsDraft).Select(b => b.Id).ToList(), cancellationToken);
        var products = (await items.FindAsync(open.Where(o => o.ProductId != null).Select(o => o.ProductId!.Value).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var rows = open.Select(o =>
        {
            var draft = boms[o.Id].FirstOrDefault(b => b.IsDraft);
            var released = boms[o.Id].FirstOrDefault(b => b.Status == BomStatus.Released);
            var product = o.ProductId is { } p ? products.GetValueOrDefault(p) : null;
            var situation = released is not null ? BomSituation.Released : draft is not null ? BomSituation.Draft : BomSituation.None;
            return new BomOrderSummary(o.Id, o.Number, o.Status, product?.Code, product?.Name, o.Quantity, o.CurrentStepCode, o.CurrentStepName, o.CurrentStepSequence, o.StepCount, situation,
                draft?.Id, draft is null ? 0 : counts.GetValueOrDefault(draft.Id), draft?.Version, released?.Id, released?.Revision, released?.ReleasedAt, WillStop(o, released is not null));
        });

        rows = query.Situation switch
        {
            "will_stop" => rows.Where(r => r.WillStop),
            "without" => rows.Where(r => r.Situation == BomSituation.None),
            "draft" => rows.Where(r => r.DraftBomId is not null),
            "released" => rows.Where(r => r.Situation == BomSituation.Released),
            _ => rows,
        };
        if (!string.IsNullOrWhiteSpace(query.Q))
        {
            var term = query.Q.Trim();
            rows = rows.Where(r => r.Number.Contains(term, StringComparison.OrdinalIgnoreCase) || (r.ProductCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.ProductName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var list = rows.OrderByDescending(r => r.WillStop).ThenBy(r => r.Number, StringComparer.Ordinal).ToList();
        return new PagedList<BomOrderSummary>(list.Skip(page.Skip).Take(page.PageSize).ToList(), page.Page, page.PageSize, list.Count);
    }

    /// <summary>A OS vai parar no próximo início de etapa sem BOM (CA 17): etapa atual depois da primeira, ou a primeira já concluída.</summary>
    public static bool WillStop(ProductionOrderInfo order, bool released)
    {
        ArgumentNullException.ThrowIfNull(order);
        return !released && order.Status is not (ProductionOrderState.ReadyToComplete or ProductionOrderState.Completed or ProductionOrderState.Cancelled)
            && (order.CurrentStepSequence > 1 || (order.FirstStepDone && order.CurrentStepSequence is not null));
    }

    /// <summary>Itens para a linha (CA 2): ativos, sem Serviço e sem o produto da OS, com as unidades usáveis e o disponível.</summary>
    public async Task<Result<IReadOnlyList<BomItemOption>>> ItemOptionsAsync(Guid productionOrderId, string? q, CancellationToken cancellationToken = default)
    {
        var order = await orders.FindAsync(productionOrderId, cancellationToken);
        if (order is null)
        {
            return OrderNotFound();
        }

        var found = (await items.SearchAsync(null, q, 40, cancellationToken))
            .Where(i => i.IsActive && !i.IsDeleted && i.Type != ItemType.Service && i.Id != order.ProductId).Take(20).ToList();
        var ids = found.Select(i => i.Id).ToList();
        var options = await units.ForItemsAsync(ids, cancellationToken);
        var positions = await stock.PositionsAsync(ids, cancellationToken);
        return found.Select(i => new BomItemOption(i.Id, i.Code, i.Name, i.Type, i.UnitId, i.UnitCode, options.GetValueOrDefault(i.Id) ?? [new ItemUnitOption(i.UnitId, i.UnitCode, true, 1m)],
            stock.Connected ? positions.GetValueOrDefault(i.Id)?.Available ?? 0 : null)).ToList();
    }

    /// <summary>
    /// Origens da cópia (CA 5, padrão 6): BOMs liberadas de outras OS, a mais nova primeiro; "Sugerido" na última OS do mesmo produto
    /// (R-PRP-1), que vem no topo.
    /// </summary>
    public async Task<Result<IReadOnlyList<BomCopySource>>> CopySourcesAsync(Guid productionOrderId, string? q, CancellationToken cancellationToken = default)
    {
        var order = await orders.FindAsync(productionOrderId, cancellationToken);
        if (order is null)
        {
            return OrderNotFound();
        }

        var released = await db.Boms.AsNoTracking().Where(b => b.Status == BomStatus.Released && b.ProductionOrderId != productionOrderId)
            .OrderByDescending(b => b.ReleasedAt).Take(200).ToListAsync(cancellationToken);
        var others = (await orders.FindManyAsync(released.Select(b => b.ProductionOrderId).Distinct().ToList(), cancellationToken)).ToDictionary(o => o.Id);
        var products = (await items.FindAsync(others.Values.Where(o => o.ProductId != null).Select(o => o.ProductId!.Value).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var counts = await LineCountsAsync(released.Select(b => b.Id).ToList(), cancellationToken);
        var suggested = order.ProductId is { } mine ? released.FirstOrDefault(b => others.GetValueOrDefault(b.ProductionOrderId)?.ProductId == mine)?.Id : null;
        var sources = released.Where(b => others.ContainsKey(b.ProductionOrderId)).Select(b =>
        {
            var other = others[b.ProductionOrderId];
            var product = other.ProductId is { } p ? products.GetValueOrDefault(p) : null;
            return new BomCopySource(b.Id, other.Id, other.Number, b.Revision, product?.Code, product?.Name, counts.GetValueOrDefault(b.Id), b.ReleasedAt!.Value, b.Id == suggested);
        });
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            sources = sources.Where(s => s.OrderNumber.Contains(term, StringComparison.OrdinalIgnoreCase) || (s.ProductCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (s.ProductName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return sources.OrderByDescending(s => s.Suggested).Take(30).ToList();
    }

    /// <summary>Comparação lado a lado de duas revisões da mesma OS (CA 15): incluídos, removidos e alterados.</summary>
    public async Task<Result<BomComparison>> CompareAsync(Guid fromId, Guid toId, CancellationToken cancellationToken = default)
    {
        var pair = await db.Boms.AsNoTracking().Where(b => b.Id == fromId || b.Id == toId).ToListAsync(cancellationToken);
        var from = pair.FirstOrDefault(b => b.Id == fromId);
        var to = pair.FirstOrDefault(b => b.Id == toId);
        if (from is null || to is null)
        {
            return BomRules.NotFound();
        }

        if (from.ProductionOrderId != to.ProductionOrderId)
        {
            return BomRules.Invalid("to", "Compare duas revisões da mesma OS.");
        }

        var lines = (await db.BomLines.AsNoTracking().Where(l => l.BomId == fromId || l.BomId == toId).ToListAsync(cancellationToken)).ToLookup(l => l.BomId);
        var itemIds = lines.SelectMany(g => g).Select(l => l.ItemId).Distinct().ToList();
        var found = (await items.FindAsync(itemIds, cancellationToken)).ToDictionary(i => i.Id);
        var codes = await units.CodesAsync(lines.SelectMany(g => g).Select(l => l.UnitId).Distinct().ToList(), cancellationToken);
        var before = lines[fromId].ToDictionary(l => l.ItemId);
        var after = lines[toId].ToDictionary(l => l.ItemId);
        var diff = BomRules.Diff(before.Values.Select(l => (l.ItemId, l.RequiredQuantity)), after.Values.Select(l => (l.ItemId, l.RequiredQuantity)));
        var result = diff.Select(d =>
        {
            var a = before.GetValueOrDefault(d.ItemId);
            var b = after.GetValueOrDefault(d.ItemId);
            // Mesma necessidade com quantidade, unidade ou perda diferentes também é mudança.
            var change = d.Change == BomChange.Unchanged && a is not null && b is not null && (a.Quantity != b.Quantity || a.UnitId != b.UnitId || a.ScrapPct != b.ScrapPct)
                ? BomChange.Changed : d.Change;
            var item = found.GetValueOrDefault(d.ItemId);
            return new BomCompareLine(d.ItemId, item?.Code ?? "—", item?.Name ?? "Item não encontrado", item?.UnitCode ?? string.Empty, change,
                a?.Quantity, a is null ? null : codes.GetValueOrDefault(a.UnitId), a?.ScrapPct, a?.RequiredQuantity,
                b?.Quantity, b is null ? null : codes.GetValueOrDefault(b.UnitId), b?.ScrapPct, b?.RequiredQuantity);
        }).OrderBy(l => l.Change == BomChange.Unchanged).ThenBy(l => l.ItemCode, StringComparer.Ordinal).ToList();
        return new BomComparison(from.Id, from.Revision, to.Id, to.Revision, result);
    }

    private async Task<BomDetail> DetailAsync(Bom bom, CancellationToken ct) => (await DetailsAsync([bom], ct))[0];

    /// <summary>
    /// Revisões com as linhas resolvidas pelos contratos: item, unidades, saldo e custo (só com <c>projects.view_cost</c>). A falta só se
    /// calcula no rascunho: na liberada o reservado já inclui a própria OS, e a falta vem das reservas (EST-02).
    /// </summary>
    private async Task<IReadOnlyList<BomDetail>> DetailsAsync(List<Bom> boms, CancellationToken ct)
    {
        if (boms.Count == 0)
        {
            return [];
        }

        var ids = boms.Select(b => b.Id).ToList();
        var lines = (await db.BomLines.AsNoTracking().Where(l => ids.Contains(l.BomId)).ToListAsync(ct)).ToLookup(l => l.BomId);
        var itemIds = lines.SelectMany(g => g).Select(l => l.ItemId).Distinct().ToList();
        var found = (await items.FindAsync(itemIds, ct)).ToDictionary(i => i.Id);
        var options = await units.ForItemsAsync(itemIds, ct);
        var codes = await units.CodesAsync(lines.SelectMany(g => g).Select(l => l.UnitId).Distinct().ToList(), ct);
        var positions = await stock.PositionsAsync(itemIds, ct);
        var seeCost = stock.Connected && await permissions.HasPermissionAsync("projects", "view_cost", ct);
        var costs = seeCost ? await stock.AverageCostsAsync(itemIds, ct) : new Dictionary<Guid, decimal>();
        var names = await NamesAsync(boms.Select(b => b.ReleasedBy), ct);
        var sourceIds = boms.Where(b => b.CopiedFromBomId != null).Select(b => b.CopiedFromBomId!.Value).Distinct().ToList();
        var sources = await db.Boms.AsNoTracking().Where(b => sourceIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var sourceOrders = (await orders.FindManyAsync(sources.Values.Select(s => s.ProductionOrderId).Distinct().ToList(), ct)).ToDictionary(o => o.Id);
        return boms.Select(b =>
        {
            var views = lines[b.Id].OrderBy(l => l.Sequence).Select(l =>
            {
                var item = found.GetValueOrDefault(l.ItemId);
                var position = positions.GetValueOrDefault(l.ItemId);
                var available = stock.Connected ? position?.Available ?? 0 : (decimal?)null;
                decimal? cost = seeCost && costs.TryGetValue(l.ItemId, out var unitCost) ? Math.Round(l.RequiredQuantity * unitCost, 2, MidpointRounding.AwayFromZero) : null;
                return new BomLineView(l.Id, l.Sequence, l.ItemId, item?.Code ?? "—", item?.Name ?? "Item não encontrado", item?.Type ?? ItemType.RawMaterial,
                    item is { IsActive: true, IsDeleted: false }, l.Quantity, l.UnitId, codes.GetValueOrDefault(l.UnitId, "?"), l.ScrapPct, l.Notes, l.RequiredQuantity,
                    item?.UnitId ?? l.UnitId, item?.UnitCode ?? "?", stock.Connected ? position?.OnHand ?? 0 : null, stock.Connected ? position?.Reserved ?? 0 : null, available,
                    b.IsDraft && available is { } free ? BomRules.Shortage(l.RequiredQuantity, free) : null, cost,
                    options.GetValueOrDefault(l.ItemId) ?? []);
            }).ToList();
            var source = b.CopiedFromBomId is { } s ? sources.GetValueOrDefault(s) : null;
            return new BomDetail(b.Id, b.ProductionOrderId, b.Revision, b.Status, b.ReleasedAt, b.ReleasedBy, b.ReleasedBy is { } by ? names.GetValueOrDefault(by) : null,
                b.CopiedFromBomId, source is null ? null : sourceOrders.GetValueOrDefault(source.ProductionOrderId)?.Number, source?.Revision, views,
                views.Count(v => v.Shortage > 0), stock.Connected, seeCost && views.All(v => v.EstimatedCost is not null) ? views.Sum(v => v.EstimatedCost!.Value) : null,
                b.Version, b.CreatedAt);
        }).ToList();
    }

    private async Task<BomOrderHeader> HeaderAsync(ProductionOrderInfo order, CancellationToken ct)
    {
        var product = order.ProductId is { } id && (await items.FindAsync([id], ct)) is [var found, ..] ? found : null;
        return new BomOrderHeader(order.Id, order.Number, order.Status, order.ProjectId, order.ProductId, product?.Code, product?.Name, order.Quantity, BomRules.Closed(order) is not null,
            order.CurrentStepCode, order.CurrentStepName, order.CurrentStepSequence);
    }

    private async Task<Dictionary<Guid, int>> LineCountsAsync(List<Guid> bomIds, CancellationToken ct) =>
        bomIds.Count == 0
            ? []
            : await db.BomLines.AsNoTracking().Where(l => bomIds.Contains(l.BomId)).GroupBy(l => l.BomId).Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var id in ids.Where(i => i != null).Select(i => i!.Value).Distinct())
        {
            if (await users.DisplayNameAsync(id, ct) is { } name)
            {
                names[id] = name;
            }
        }

        return names;
    }

    internal static Error OrderNotFound() => Error.NotFound("production_order_not_found", "OS de fabricação não encontrada.");
}
