using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Inventory.Application;

/// <summary>Quantidade de produto acabado atribuída a um projeto (ou ao estoque geral, sem projeto) numa posição item/local/lote.</summary>
public sealed record AttributedStock(Guid? ProjectId, Guid ItemId, Guid LocationId, Guid? LotId, bool TracksSerial, decimal Quantity, IReadOnlyList<SerialInfo> Serials);

/// <summary>Físico e reservado da posição (item, local), para limitar o que dá para expedir.</summary>
public sealed record PositionStock(decimal OnHand, decimal Reserved);

/// <summary>
/// Produto acabado pronto para expedir (PRD-06b, atrás de <see cref="IFinishedGoodsStock"/>): só leitura. Com série, vale a série
/// <c>InStock</c> com o projeto; sem série, a soma das entradas <c>Production</c> e das saídas <c>Shipment</c> (com os estornos, que
/// repetem o tipo com o sinal oposto) com o projeto, por item, local e lote. O estoque geral é o físico do produto acabado menos o que é
/// de algum projeto. Nunca grava: a saída é do <see cref="StockLedger"/> e a série do <see cref="StockSerialService"/>.
/// </summary>
public sealed class FinishedGoodsStockService(IInventoryDb db) : IFinishedGoodsStock
{
    public async Task<IReadOnlyList<FinishedGoodsStockLine>> ForProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await LinesAsync(await AttributedAsync(projectId, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<FinishedGoodsStockLine>> ReadyToShipAsync(int limit = 500, CancellationToken cancellationToken = default) =>
        (await LinesAsync(await AttributedAsync(null, cancellationToken), cancellationToken)).Take(Math.Clamp(limit, 1, 2000)).ToList();

    public async Task<IReadOnlyList<FinishedGoodsStockLine>> GeneralStockAsync(string? term, int limit = 50, CancellationToken cancellationToken = default)
    {
        var items = db.Items.AsNoTracking().Where(i => i.Type == ItemType.Finished && i.IsActive);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term.Trim()}%";
            items = items.Where(i => EF.Functions.ILike(i.Code, pattern) || EF.Functions.ILike(i.Name, pattern));
        }

        var found = await items.OrderBy(i => i.Code).Select(i => new { i.Id, i.TracksSerial }).Take(Math.Clamp(limit, 1, 200)).ToListAsync(cancellationToken);
        if (found.Count == 0)
        {
            return [];
        }

        var ids = found.Select(i => i.Id).ToList();
        var serialItems = found.Where(i => i.TracksSerial).Select(i => i.Id).ToHashSet();
        var balances = await db.StockBalances.AsNoTracking().Where(b => ids.Contains(b.ItemId) && b.QuantityOnHand > 0)
            .Select(b => new { b.ItemId, b.LocationId, b.LotId, b.QuantityOnHand }).ToListAsync(cancellationToken);
        var attributed = await NetAsync(null, ids, cancellationToken);
        var general = new List<AttributedStock>();
        foreach (var b in balances.Where(b => !serialItems.Contains(b.ItemId)))
        {
            var ofProjects = attributed.Where(a => a.ItemId == b.ItemId && a.LocationId == b.LocationId && a.LotId == b.LotId).Sum(a => a.Quantity);
            general.Add(new AttributedStock(null, b.ItemId, b.LocationId, b.LotId, false, b.QuantityOnHand - Math.Max(ofProjects, 0m), []));
        }

        // Série sem projeto (produto acabado lançado fora de uma OS): a unidade é do estoque geral.
        var loose = await db.Serials.AsNoTracking().Where(s => ids.Contains(s.ItemId) && s.Status == SerialState.InStock && s.ProjectId == null && s.LocationId != null)
            .ToListAsync(cancellationToken);
        general.AddRange(await SerialGroupsAsync(loose, cancellationToken));
        return await LinesAsync(general, cancellationToken);
    }

    /// <summary>
    /// Junta o que é do projeto com o físico da posição (CA 1 e 13): o que dá para expedir é o menor entre o do projeto, o disponível da
    /// posição (físico − reservado) e o físico do lote; linha sem nada do projeto fica fora. Sem banco, para o teste de unidade.
    /// </summary>
    public static IReadOnlyList<FinishedGoodsStockLine> Combine(
        IEnumerable<AttributedStock> attributed, IReadOnlyDictionary<(Guid Item, Guid Location), PositionStock> positions, IReadOnlyDictionary<(Guid Lot, Guid Location), decimal> lotOnHand,
        IReadOnlyDictionary<Guid, string> lotNumbers)
    {
        ArgumentNullException.ThrowIfNull(attributed);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(lotOnHand);
        ArgumentNullException.ThrowIfNull(lotNumbers);
        var lines = new List<FinishedGoodsStockLine>();
        foreach (var a in attributed.Where(a => a.Quantity > 0))
        {
            var position = positions.GetValueOrDefault((a.ItemId, a.LocationId)) ?? new PositionStock(0m, 0m);
            var available = position.OnHand - position.Reserved;
            var cap = a.LotId is { } lot ? Math.Min(available, lotOnHand.GetValueOrDefault((lot, a.LocationId))) : available;
            var shippable = Math.Max(0m, Math.Min(a.Quantity, cap));
            lines.Add(new FinishedGoodsStockLine(
                a.ProjectId, a.ItemId, a.LocationId, a.LotId, a.LotId is { } l ? lotNumbers.GetValueOrDefault(l) : null, a.TracksSerial, a.Quantity, position.OnHand, position.Reserved,
                available, a.TracksSerial ? Math.Min(a.Serials.Count, shippable) : shippable, a.Serials.OrderBy(s => s.SerialNumber, StringComparer.Ordinal).ToList()));
        }

        return lines.OrderBy(l => l.ProjectId).ThenBy(l => l.ItemId).ThenBy(l => l.LocationId).ThenBy(l => l.LotId).ToList();
    }

    /// <summary>Séries no estoque com projeto e o saldo sem série que veio das OS do projeto (ou de todos os projetos).</summary>
    private async Task<List<AttributedStock>> AttributedAsync(Guid? projectId, CancellationToken ct)
    {
        var serials = db.Serials.AsNoTracking().Where(s => s.Status == SerialState.InStock && s.ProjectId != null && s.LocationId != null);
        if (projectId is { } project)
        {
            serials = serials.Where(s => s.ProjectId == project);
        }

        var result = (await SerialGroupsAsync(await serials.ToListAsync(ct), ct)).ToList();
        result.AddRange((await NetAsync(projectId, null, ct)).Where(a => a.Quantity > 0));
        return result;
    }

    /// <summary>Entradas <c>Production</c> + saídas <c>Shipment</c> com projeto, dos itens sem série, por projeto, item, local e lote.</summary>
    private async Task<List<AttributedStock>> NetAsync(Guid? projectId, IReadOnlyCollection<Guid>? itemIds, CancellationToken ct)
    {
        var movements = db.StockMovements.AsNoTracking()
            .Where(m => m.ProjectId != null && (m.Type == StockMovementType.Production || m.Type == StockMovementType.Shipment))
            .Where(m => !db.Items.Any(i => i.Id == m.ItemId && i.TracksSerial));
        if (projectId is { } project)
        {
            movements = movements.Where(m => m.ProjectId == project);
        }

        if (itemIds is not null)
        {
            movements = movements.Where(m => itemIds.Contains(m.ItemId));
        }

        var rows = await movements.GroupBy(m => new { m.ProjectId, m.ItemId, m.LocationId, m.LotId })
            .Select(g => new { g.Key.ProjectId, g.Key.ItemId, g.Key.LocationId, g.Key.LotId, Quantity = g.Sum(m => m.Quantity) })
            .ToListAsync(ct);
        return rows.Select(r => new AttributedStock(r.ProjectId, r.ItemId, r.LocationId, r.LotId, false, r.Quantity, [])).ToList();
    }

    /// <summary>Agrupa as séries por projeto, item, local e lote (o lote da série é o do movimento de entrada).</summary>
    private async Task<IEnumerable<AttributedStock>> SerialGroupsAsync(List<Serial> serials, CancellationToken ct)
    {
        if (serials.Count == 0)
        {
            return [];
        }

        var entries = serials.Select(s => s.EntryMovementId).OfType<Guid>().Distinct().ToList();
        var lots = await db.StockMovements.AsNoTracking().Where(m => entries.Contains(m.Id)).Select(m => new { m.Id, m.LotId }).ToDictionaryAsync(m => m.Id, m => m.LotId, ct);
        return serials
            .GroupBy(s => (s.ProjectId, s.ItemId, Location: s.LocationId!.Value, Lot: s.EntryMovementId is { } e ? lots.GetValueOrDefault(e) : null))
            .Select(g => new AttributedStock(g.Key.ProjectId, g.Key.ItemId, g.Key.Location, g.Key.Lot, true, g.Count(), g.Select(StockSerialService.Info).ToList()));
    }

    private async Task<IReadOnlyList<FinishedGoodsStockLine>> LinesAsync(List<AttributedStock> attributed, CancellationToken ct)
    {
        if (attributed.Count == 0)
        {
            return [];
        }

        var itemIds = attributed.Select(a => a.ItemId).Distinct().ToList();
        var positions = await db.StockPositions.AsNoTracking().Where(p => itemIds.Contains(p.ItemId))
            .Select(p => new { p.ItemId, p.LocationId, p.QuantityOnHand, p.QuantityReserved }).ToListAsync(ct);
        var lotIds = attributed.Select(a => a.LotId).OfType<Guid>().Distinct().ToList();
        var lotOnHand = lotIds.Count == 0 ? new Dictionary<(Guid Lot, Guid Location), decimal>()
            : (await db.StockBalances.AsNoTracking().Where(b => b.LotId != null && lotIds.Contains(b.LotId.Value))
                .Select(b => new { Lot = b.LotId!.Value, b.LocationId, b.QuantityOnHand }).ToListAsync(ct))
                .GroupBy(b => (b.Lot, b.LocationId)).ToDictionary(g => g.Key, g => g.Sum(b => b.QuantityOnHand));
        var lotNumbers = lotIds.Count == 0 ? new Dictionary<Guid, string>()
            : await db.Lots.AsNoTracking().Where(l => lotIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.LotNumber, ct);
        return Combine(attributed, positions.ToDictionary(p => (p.ItemId, p.LocationId), p => new PositionStock(p.QuantityOnHand, p.QuantityReserved)), lotOnHand, lotNumbers);
    }
}
