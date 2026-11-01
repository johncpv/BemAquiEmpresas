using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Time;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// <see cref="IMaintenanceDowntimes"/> (PAT-03b CA 8 a 11, ADR 0038 §4): o estado atual da parada de cada OS, calculado da OS e do bem (centro
/// atual), para o consumidor do Production reconciliar pela fonte. Só datas, horas e ids.
/// </summary>
public sealed class MaintenanceDowntimesContract(IAssetsDb db) : IMaintenanceDowntimes
{
    public async Task<MaintenanceDowntimeState?> FindAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        await FindManyAsync([orderId], cancellationToken) is [var state, ..] ? state : null;

    public async Task<IReadOnlyList<MaintenanceDowntimeState>> FindManyAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        if (orderIds.Count == 0)
        {
            return [];
        }

        var ids = orderIds.Distinct().ToList();
        var rows = await (from o in db.MaintenanceOrders.AsNoTracking()
                          join a in db.Assets.IgnoreQueryFilters().AsNoTracking() on o.AssetId equals a.Id
                          where ids.Contains(o.Id)
                          select new { Order = o, a.WorkCenterId }).ToListAsync(cancellationToken);
        return rows.Select(r => State(r.Order, r.WorkCenterId)).ToList();
    }

    /// <summary>
    /// A parada da OS hoje: sem período (bem sem centro, sem horas), cancelada antes de começar ou concluída antes do início, sai; concluída ou
    /// cancelada depois do início, termina na data; aberta com a máquina parada ou em andamento, segue até a conclusão.
    /// </summary>
    public static MaintenanceDowntimeState State(MaintenanceOrder o, Guid? workCenterId)
    {
        ArgumentNullException.ThrowIfNull(o);
        var from = o.DowntimeFrom ?? o.ScheduledOn;
        var to = o.DowntimeTo ?? from;
        DateOnly? ended = o.Status switch
        {
            MaintenanceOrderStatus.Done when o.DoneAt is { } done => Date(done),
            MaintenanceOrderStatus.Cancelled when o.CancelledAt is { } cancelled => MaintenanceRules.CancelledDowntimeEnd(from, Date(cancelled)),
            _ => null,
        };
        var removed = o.DowntimeFrom is null || o.HoursPerDay is not > 0 || workCenterId is null
            || (o.Status == MaintenanceOrderStatus.Cancelled && ended is null)
            || (o.Status == MaintenanceOrderStatus.Done && ended < from);
        return new MaintenanceDowntimeState(o.Id, o.Number, o.AssetId, workCenterId, from, to, o.HoursPerDay ?? 0m, MaintenanceRules.HoldsAsset(o.Status, o.MachineStopped), ended, removed,
            o.DowntimeRevision);
    }

    private static DateOnly Date(DateTimeOffset at) => DateOnly.FromDateTime(BusinessTime.ToBusiness(at).DateTime);
}
