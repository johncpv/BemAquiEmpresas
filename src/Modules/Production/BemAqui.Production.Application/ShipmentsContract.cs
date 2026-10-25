using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// <see cref="IShipments"/> (PRD-06b CA 12 e 13): os romaneios com as linhas e as séries, lidos só das tabelas da Produção (sem depender de
/// outros contratos, para o Commercial usar sem ciclo no DI).
/// </summary>
public sealed class ShipmentsContract(IProductionDb db) : IShipments
{
    public async Task<ShipmentInfo?> ByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await InfosAsync(db.Shipments.AsNoTracking().Where(s => s.Id == id), cancellationToken) is [var found, ..] ? found : null;

    public async Task<IReadOnlyList<ShipmentInfo>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await InfosAsync(db.Shipments.AsNoTracking().Where(s => s.ProjectId == projectId), cancellationToken);

    private async Task<List<ShipmentInfo>> InfosAsync(IQueryable<Shipment> query, CancellationToken ct)
    {
        var shipments = await query.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id).ToListAsync(ct);
        var ids = shipments.Select(s => s.Id).ToList();
        var lines = await db.ShipmentLines.AsNoTracking().Where(l => ids.Contains(l.ShipmentId)).OrderBy(l => l.Sequence).ToListAsync(ct);
        var serials = await db.ShipmentLineSerials.AsNoTracking().Where(s => ids.Contains(s.ShipmentId)).OrderBy(s => s.SerialNumber).ToListAsync(ct);
        return shipments.Select(s => new ShipmentInfo(
            s.Id, s.Number, s.ProjectId, s.PartyId, s.Status, s.PlannedDate, s.CreatedAt, s.ShippedAt, s.ShippedBy, s.ReversedAt, s.ReversedBy, s.ReversalReason,
            lines.Where(l => l.ShipmentId == s.Id).Select(l =>
            {
                var own = serials.Where(x => x.LineId == l.Id).ToList();
                return new ShipmentLineInfo(l.Id, l.Sequence, l.ItemId, l.Quantity, l.LocationId, l.LotId, l.FromGeneralStock, own.Select(x => x.SerialId).ToList(),
                    own.Select(x => x.SerialNumber).ToList(), l.MovementId);
            }).ToList())).ToList();
    }
}
