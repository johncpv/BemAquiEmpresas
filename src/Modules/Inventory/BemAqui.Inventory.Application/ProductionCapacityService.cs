using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Engineering.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using BemAqui.Purchasing.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Capacidade de produzir (EST-06, R-EST-10): para a BOM liberada de uma OS (a escolhida ou a última do produto, "Sugerido"), quantas
/// unidades o disponível de hoje permite e o que falta para a quantidade desejada. Só lê: a BOM pelo <see cref="IBoms"/>, a OS pelo
/// <see cref="IProductionOrders"/>, o disponível pela posição agregada (<see cref="IStockBalances"/>, ADR 0023 §2) e, com
/// <c>purchasing.view</c> e o módulo liberado, o saldo a receber pelo <see cref="IPurchaseOrders.OpenQuantityByItemAsync"/> (destino
/// "todos"). Uma chamada por contrato, em lote (CA 7). A conversão de unidade e a perda são refeitas aqui, com as conversões do item
/// (item sem conversão vira "não calculado", CA 5).
/// </summary>
public sealed class ProductionCapacityService(
    IInventoryDb db, IBoms boms, IProductionOrders orders, IStockBalances balances, IPurchaseOrders purchaseOrders, IPermissionChecker permissions,
    TenantContextHolder tenant)
{
    /// <summary>Quantas BOMs liberadas (as mais novas) entram na escolha da referência.</summary>
    public const int ReferenceWindow = 200;

    public const int MaxReferences = 50;

    /// <summary>
    /// O grupo da rota exige só o módulo <c>inventory</c>; a capacidade lê a BOM, então o módulo <c>engineering</c> precisa estar liberado no
    /// tenant (403 <c>module_not_released</c>), conferido aqui (CA 6). A permissão <c>engineering.view</c> fica na rota.
    /// </summary>
    public Error? EngineeringNotReleased() =>
        tenant.Resolved?.IsReleased("engineering") == true ? null : Error.Forbidden(Error.Codes.ModuleNotReleased, "Funcionalidade não liberada nessa versão do sistema.");

    /// <summary>BOMs de referência (CA 1): as liberadas vigentes, a mais nova primeiro; "Sugerido" na última de cada produto.</summary>
    public async Task<IReadOnlyList<CapacityReference>> ReferencesAsync(string? q, Guid? productId, CancellationToken cancellationToken = default)
    {
        IEnumerable<CapacityReference> found = await AllReferencesAsync(cancellationToken);
        if (productId is { } product)
        {
            found = found.Where(r => r.ProductId == product);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            found = found.Where(r => r.OrderNumber.Contains(term, StringComparison.OrdinalIgnoreCase) || (r.ProductCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
                || (r.ProductName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return found.Take(MaxReferences).ToList();
    }

    /// <summary>
    /// Calcula a capacidade (CA 1 a 5) para a BOM liberada da OS (<paramref name="productionOrderId"/>) ou, sem OS, para a última liberada
    /// do produto (<paramref name="productId"/>, marcada "Sugerido"). Quantidade desejada padrão 1.
    /// </summary>
    public async Task<Result<ProductionCapacityView>> CalculateAsync(Guid? productionOrderId, Guid? productId, decimal? quantity, CancellationToken cancellationToken = default)
    {
        if (EngineeringNotReleased() is { } notReleased)
        {
            return notReleased;
        }

        var wanted = quantity ?? 1m;
        if (ProductionCapacityCalculator.ValidateQuantity(wanted) is { } invalid)
        {
            return invalid;
        }

        var suggested = false;
        var orderId = productionOrderId;
        if (orderId is null)
        {
            if (productId is not { } product)
            {
                return Error.Validation("validation", "Escolha um produto ou uma OS.", new Dictionary<string, string[]> { ["productId"] = ["Escolha um produto ou uma OS."] });
            }

            var latest = (await AllReferencesAsync(cancellationToken)).FirstOrDefault(r => r.ProductId == product);
            if (latest is null)
            {
                return Error.NotFound("capacity_reference_not_found", "Nenhuma OS deste produto tem BOM liberada. Escolha outra OS ou libere a BOM na aba BOM da OS.");
            }

            orderId = latest.ProductionOrderId;
            suggested = true;
        }

        var order = await orders.FindAsync(orderId.Value, cancellationToken);
        if (order is null)
        {
            return Error.NotFound("production_order_not_found", "OS de fabricação não encontrada.");
        }

        var bom = await boms.ReleasedForOrderAsync(order.Id, cancellationToken);
        if (bom is null)
        {
            return Error.NotFound("bom_not_released", $"A OS {order.Number} não tem BOM liberada. Escolha outra OS ou libere a BOM na aba BOM da OS.");
        }

        var itemIds = bom.Lines.Select(l => l.ItemId).Append(order.ProductId ?? Guid.Empty).Where(i => i != Guid.Empty).Distinct().ToList();
        var items = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .Select(i => new ItemRow(i.Id, i.Code, i.Name, i.UnitId, i.IsActive, i.IsDeleted, i.AverageCost)).ToDictionaryAsync(i => i.Id, cancellationToken);
        var conversions = (await db.ItemUnitConversions.AsNoTracking().Where(c => itemIds.Contains(c.ItemId)).ToListAsync(cancellationToken)).ToLookup(c => c.ItemId);
        var unitIds = items.Values.Select(i => i.UnitId).Concat(bom.Lines.Select(l => l.UnitId)).Where(u => u != Guid.Empty).Distinct().ToList();
        var codes = await db.Units.IgnoreQueryFilters().AsNoTracking().Where(u => unitIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Code, cancellationToken);
        var materialIds = bom.Lines.Select(l => l.ItemId).Distinct().ToList();
        Dictionary<Guid, (decimal OnHand, decimal Reserved, decimal Available)> positions = materialIds.Count == 0 ? [] : (await balances.GetAsync(materialIds, null, cancellationToken)).GroupBy(r => r.ItemId)
            .ToDictionary(g => g.Key, g => (OnHand: g.Sum(r => r.OnHand), Reserved: g.Sum(r => r.Reserved), Available: g.Sum(r => r.Available)));
        var showOpen = materialIds.Count > 0 && tenant.Resolved?.IsReleased("purchasing") == true && await permissions.HasPermissionAsync("purchasing", "view", cancellationToken);
        var open = showOpen ? await purchaseOrders.OpenQuantityByItemAsync(materialIds, PurchaseOrderDestination.All, cancellationToken) : null;
        var showCost = await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken);

        var prepared = bom.Lines.Select(l => Prepare(l, order, items.GetValueOrDefault(l.ItemId), conversions[l.ItemId].ToList(), codes)).ToList();
        var inputs = prepared.Select(p =>
        {
            var position = positions.GetValueOrDefault(p.Line.ItemId);
            return new CapacityInput(p.PerUnit, position.Available, open is null ? null : open.GetValueOrDefault(p.Line.ItemId),
                showCost && p.Item is { } item ? item.AverageCost : null);
        }).ToList();
        var result = ProductionCapacityCalculator.Calculate(inputs, wanted);

        var lines = prepared.Select((p, i) =>
        {
            var position = positions.GetValueOrDefault(p.Line.ItemId);
            var r = result.Lines[i];
            return new CapacityLine(p.Line.BomLineId, p.Line.ItemId, p.Item?.Code ?? "—", p.Item?.Name ?? "Item não encontrado",
                p.Item?.UnitId, p.Item is { } item ? codes.GetValueOrDefault(item.UnitId, "?") : "?", p.PerUnit, position.OnHand, position.Reserved, position.Available,
                r.Possible, r.Required, r.Shortage, open is null ? null : open.GetValueOrDefault(p.Line.ItemId), showCost ? r.ShortageCost : null, r.Limiting, p.Warning);
        }).ToList();
        var limitingLine = result.LimitingIndex is { } index ? lines[index] : null;
        var productRow = order.ProductId is { } pid ? items.GetValueOrDefault(pid) : null;
        var reference = new CapacityReference(bom.BomId, order.Id, order.Number, bom.Revision, bom.ReleasedAt, order.ProductId, productRow?.Code, productRow?.Name, bom.Lines.Count, suggested);
        return new ProductionCapacityView(reference, wanted, result.Possible, limitingLine?.ItemId, limitingLine?.ItemName, result.ShortageCount, result.NotCalculatedCount,
            showOpen, showOpen ? result.PossibleWithOpen : null, showCost, showCost ? result.ShortageCost : null, lines);
    }

    /// <summary>
    /// Necessidade por unidade do produto na unidade de estoque e o aviso da linha (CA 5). A linha sem a quantidade por unidade (mensagem
    /// antiga do contrato) usa a necessidade da OS dividida pela quantidade da OS, que já está na unidade de estoque.
    /// </summary>
    private static PreparedLine Prepare(BomRequirement line, ProductionOrderInfo order, ItemRow? item, List<ItemUnitConversion> conversions, Dictionary<Guid, string> codes)
    {
        if (item is null || item.IsDeleted)
        {
            return new PreparedLine(line, item, null, "Item não encontrado no estoque (excluído): não entra no cálculo. Troque o item na BOM.");
        }

        if (!item.IsActive)
        {
            return new PreparedLine(line, item, null, "Item inativo: não entra no cálculo. Reative o item em Estoque → Itens ou troque na BOM.");
        }

        if (line.QuantityPerUnit <= 0)
        {
            return order.Quantity > 0 && line.RequiredQty > 0
                ? new PreparedLine(line, item, ProductionCapacityCalculator.Round(line.RequiredQty / order.Quantity), null)
                : new PreparedLine(line, item, null, "A BOM não diz a quantidade por unidade deste item: não entra no cálculo.");
        }

        var unit = line.UnitId == Guid.Empty ? item.UnitId : line.UnitId;
        var factor = UnitConverter.ToStockFactor(unit, item.UnitId, conversions);
        if (factor is null)
        {
            return new PreparedLine(line, item, null,
                $"Sem conversão de {codes.GetValueOrDefault(unit, "?")} para {codes.GetValueOrDefault(item.UnitId, "?")}: não entra no cálculo. Cadastre em Estoque → Itens → Conversões.");
        }

        return ProductionCapacityCalculator.PerUnit(line.QuantityPerUnit, line.ScrapPct, factor) is { } perUnit && perUnit > 0
            ? new PreparedLine(line, item, perUnit, null)
            : new PreparedLine(line, item, null, "A necessidade por unidade fica abaixo de 0,0001 na unidade de estoque: não entra no cálculo.");
    }

    /// <summary>As referências da janela, com a OS e o produto; "Sugerido" na mais nova de cada produto.</summary>
    private async Task<List<CapacityReference>> AllReferencesAsync(CancellationToken ct)
    {
        var released = await boms.ReleasedAsync(ReferenceWindow, ct);
        if (released.Count == 0)
        {
            return [];
        }

        var found = (await orders.FindManyAsync(released.Select(r => r.ProductionOrderId).Distinct().ToList(), ct)).ToDictionary(o => o.Id);
        var productIds = found.Values.Where(o => o.ProductId != null).Select(o => o.ProductId!.Value).Distinct().ToList();
        var products = await db.Items.IgnoreQueryFilters().AsNoTracking().Where(i => productIds.Contains(i.Id)).Select(i => new { i.Id, i.Code, i.Name }).ToDictionaryAsync(i => i.Id, ct);
        var seen = new HashSet<Guid>();
        var references = new List<CapacityReference>();
        foreach (var bom in released.OrderByDescending(r => r.ReleasedAt))
        {
            if (!found.TryGetValue(bom.ProductionOrderId, out var order))
            {
                continue;
            }

            var product = order.ProductId is { } p ? products.GetValueOrDefault(p) : null;
            var suggested = order.ProductId is { } id && seen.Add(id);
            references.Add(new CapacityReference(bom.BomId, order.Id, order.Number, bom.Revision, bom.ReleasedAt, order.ProductId, product?.Code, product?.Name, bom.LineCount, suggested));
        }

        return references;
    }

    private sealed record ItemRow(Guid Id, string Code, string Name, Guid UnitId, bool IsActive, bool IsDeleted, decimal AverageCost);

    private sealed record PreparedLine(BomRequirement Line, ItemRow? Item, decimal? PerUnit, string? Warning);
}
