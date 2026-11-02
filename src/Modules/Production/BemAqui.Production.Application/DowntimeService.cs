using BemAqui.Assets.Contracts;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>Parada por manutenção na tela do centro (CAD-03, PAT-03b CA 10): a OS, o bem, o período efetivo e as horas por dia.</summary>
public sealed record WorkCenterDowntimeView(
    Guid Id, Guid OrderId, string? OrderNumber, Guid? AssetId, string? AssetTag, string? AssetName, DateOnly StartsOn, DateOnly EndsOn, decimal HoursPerDay, bool Ongoing, bool Ended);

/// <summary>
/// Paradas do centro de trabalho (PAT-03b, ADR 0038 §4). O Production é o dono e o único escritor de <c>production.downtimes</c>: o consumidor
/// dos eventos da manutenção chama <see cref="ReconcileAsync"/>, que lê o estado atual da parada na fonte (<see cref="IMaintenanceDowntimes"/>,
/// nunca o delta do evento) e grava, muda ou retira a linha única da OS; reentrega e ordem trocada caem no mesmo estado (CA 11). Toda mudança
/// pede o recálculo agrupado do plano (PLN-01b), e o <c>IWorkCenterCapacity</c> já devolve a capacidade descontada (CA 10).
/// </summary>
public sealed class DowntimeService(IProductionDb db, IMaintenanceDowntimes source, IAssets assets, IScheduleTrigger trigger, TimeProvider clock)
{
    /// <summary>Janela padrão da tela do centro: de 30 dias atrás a 180 dias à frente.</summary>
    public const int PastDays = 30;
    public const int FutureDays = 180;

    /// <summary>Reconcilia a parada da OS com a fonte; devolve se mudou (e o recálculo foi pedido).</summary>
    public async Task<bool> ReconcileAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var state = await source.FindAsync(orderId, cancellationToken);
        var current = await db.Downtimes.FirstOrDefaultAsync(d => d.SourceType == Downtime.MaintenanceSource && d.SourceId == orderId, cancellationToken);
        if (current is not null && state is not null && state.Revision < current.SourceRevision)
        {
            return false;
        }

        var gone = state is null || state.Removed || state.WorkCenterId is null || state.HoursPerDay <= 0;
        if (gone)
        {
            if (current is null)
            {
                return false;
            }

            db.Downtimes.Remove(current);
        }
        else if (current is null)
        {
            db.Downtimes.Add(new Downtime
            {
                SourceId = orderId, WorkCenterId = state!.WorkCenterId!.Value, AssetId = state.AssetId, StartsOn = state.From, PlannedEndOn = Max(state.To, state.From),
                EndedOn = state.EndedOn, OpenEnded = state.OpenEnded, HoursPerDay = state.HoursPerDay, SourceRevision = state.Revision, CreatedAt = clock.GetUtcNow(),
                UpdatedAt = clock.GetUtcNow(),
            });
        }
        else
        {
            var s = state!;
            var end = Max(s.To, s.From);
            if (current.WorkCenterId == s.WorkCenterId && current.AssetId == s.AssetId && current.StartsOn == s.From && current.PlannedEndOn == end && current.EndedOn == s.EndedOn
                && current.OpenEnded == s.OpenEnded && current.HoursPerDay == s.HoursPerDay)
            {
                current.SourceRevision = Math.Max(current.SourceRevision, s.Revision);
                return false;
            }

            current.WorkCenterId = s.WorkCenterId!.Value;
            current.AssetId = s.AssetId;
            current.StartsOn = s.From;
            current.PlannedEndOn = end;
            current.EndedOn = s.EndedOn;
            current.OpenEnded = s.OpenEnded;
            current.HoursPerDay = s.HoursPerDay;
            current.SourceRevision = s.Revision;
            current.UpdatedAt = clock.GetUtcNow();
        }

        await trigger.RequestAsync("manutenção", cancellationToken);
        return true;
    }

    /// <summary>"Paradas por manutenção" do centro (CA 10): as que valem no período (padrão: 30 dias atrás a 180 à frente), pelo início.</summary>
    public async Task<Result<IReadOnlyList<WorkCenterDowntimeView>>> ForCenterAsync(Guid workCenterId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default)
    {
        if (!await db.WorkCenters.AsNoTracking().AnyAsync(w => w.Id == workCenterId, cancellationToken))
        {
            return Error.NotFound("work_center_not_found", "Centro de trabalho não encontrado.");
        }

        var today = BusinessTime.Today(clock);
        var start = from ?? today.AddDays(-PastDays);
        var end = to ?? today.AddDays(FutureDays);
        if (end < start || end.DayNumber - start.DayNumber > CapacityCalculator.MaxDays)
        {
            return Error.Validation(Error.Codes.Validation, "Escolha um período de até 2 anos, com o fim depois do início.");
        }

        var rows = (await OverlappingAsync(workCenterId, start, end, cancellationToken)).OrderBy(d => d.StartsOn).ToList();
        var orders = rows.Count == 0 ? [] : (await source.FindManyAsync(rows.Select(r => r.SourceId).ToList(), cancellationToken)).ToDictionary(o => o.OrderId);
        var assetIds = rows.Select(r => r.AssetId).OfType<Guid>().Distinct().ToList();
        var names = assetIds.Count == 0 ? [] : (await assets.FindAsync(assetIds, cancellationToken)).ToDictionary(a => a.Id);
        return rows.Select(r =>
        {
            var effective = DowntimeRules.EffectiveEnd(r.PlannedEndOn, r.EndedOn, r.OpenEnded, today);
            var asset = r.AssetId is { } a ? names.GetValueOrDefault(a) : null;
            return new WorkCenterDowntimeView(r.Id, r.SourceId, orders.GetValueOrDefault(r.SourceId)?.Number, r.AssetId, asset?.Tag, asset?.Name, r.StartsOn, effective, r.HoursPerDay,
                r.EndedOn is null && r.StartsOn <= today && effective >= today, r.EndedOn is not null);
        }).ToList();
    }

    /// <summary>Paradas do centro que tocam o período (o fim efetivo da máquina parada sem fim real vai até hoje).</summary>
    internal async Task<List<Downtime>> OverlappingAsync(Guid workCenterId, DateOnly from, DateOnly to, CancellationToken ct) =>
        await OverlappingQuery(db, workCenterId, from, to).AsNoTracking().ToListAsync(ct);

    internal static IQueryable<Downtime> OverlappingQuery(IProductionDb db, Guid workCenterId, DateOnly from, DateOnly to) =>
        db.Downtimes.Where(d => d.WorkCenterId == workCenterId && d.StartsOn <= to && (d.EndedOn != null ? d.EndedOn >= from : d.OpenEnded || d.PlannedEndOn >= from));

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
}

/// <summary>
/// Eventos da manutenção (PAT-03b CA 8 a 11, ADR 0038 §4): agendada, reagendada, cancelada ou concluída, a parada do centro é reconciliada pela
/// fonte. Idempotente pela inbox e pela linha única por OS; o evento mais velho não desfaz o mais novo (a fonte é a OS de hoje).
/// </summary>
public sealed class MaintenanceDowntimeConsumers(DowntimeService downtimes)
    : IIntegrationEventConsumer<MaintenanceScheduled>, IIntegrationEventConsumer<MaintenanceUnscheduled>, IIntegrationEventConsumer<MaintenanceCompleted>
{
    public Task HandleAsync(MaintenanceScheduled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return downtimes.ReconcileAsync(integrationEvent.OrderId, cancellationToken);
    }

    public Task HandleAsync(MaintenanceUnscheduled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return downtimes.ReconcileAsync(integrationEvent.OrderId, cancellationToken);
    }

    public Task HandleAsync(MaintenanceCompleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return downtimes.ReconcileAsync(integrationEvent.OrderId, cancellationToken);
    }
}
