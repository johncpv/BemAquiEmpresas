using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Purchasing.Contracts;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Faltas de material do painel do supervisor (DSH-03 CA 5): as faltas abertas das OS (<c>IStockReservations</c>, EST-02), o início previsto da
/// etapa que consome o material (<c>IProductionSchedule</c>, a próxima etapa não concluída da OS), os dias úteis pelo calendário padrão
/// (<c>IBusinessCalendar</c>) e a situação da compra pelo Purchasing (<c>IPurchaseRequests</c> e <c>IPurchaseOrders</c>, só leitura, sem
/// <c>purchasing.view</c>). Os nomes (item e projeto) são lidos só para a página mostrada (CA 8).
/// </summary>
public sealed class SupervisorShortages(
    SupervisorService supervisor,
    IStockReservations reservations,
    IProductionOrders orders,
    IProductionBoard board,
    IProductionSchedule schedule,
    IBusinessCalendar calendar,
    IPurchaseRequests requests,
    IPurchaseOrders purchaseOrders,
    IItems items,
    IProjects projects,
    TimeProvider clock)
{
    private sealed record Row(Guid OrderId, string OrderNumber, Guid ProjectId, Guid ItemId, decimal ShortQty, DateOnly? NeededOn, int? BlocksIn);

    public async Task<SupervisorShortagesBlock> ListAsync(string? line, CancellationToken cancellationToken = default)
    {
        var today = BusinessTime.Today(clock);
        var shortages = (await reservations.OpenShortagesAsync(cancellationToken)).Where(s => s.ShortQty > 0).ToList();
        var orderIds = shortages.Select(s => s.ProductionOrderId).Distinct().ToList();
        var open = (await orders.FindManyAsync(orderIds, cancellationToken))
            .Where(o => o.Status is not (ProductionOrderState.Completed or ProductionOrderState.Cancelled)).ToDictionary(o => o.Id);
        if (!string.IsNullOrWhiteSpace(line))
        {
            var lines = await board.LinesOfOrdersAsync([.. open.Keys], cancellationToken);
            open = open.Where(o => string.Equals(lines.GetValueOrDefault(o.Key)?.Trim(), line.Trim(), StringComparison.Ordinal)).ToDictionary(o => o.Key, o => o.Value);
        }

        var starts = (await schedule.ForOrdersAsync([.. open.Keys], cancellationToken)).ToDictionary(f => f.OrderId, f => f.PlannedStart);
        var workingDates = await calendar.WorkingDatesAsync(today.AddDays(1), today.AddDays(SupervisorBlocks.CalendarWindowDays), cancellationToken);
        var rows = shortages.Where(s => open.ContainsKey(s.ProductionOrderId))
            .GroupBy(s => (s.ProductionOrderId, s.ItemId))
            .Select(g =>
            {
                var start = starts.GetValueOrDefault(g.Key.ProductionOrderId);
                return new Row(g.Key.ProductionOrderId, open[g.Key.ProductionOrderId].Number, g.First().ProjectId, g.Key.ItemId, g.Sum(s => s.ShortQty), start,
                    SupervisorBlocks.WorkingDaysUntil(today, start, workingDates));
            })
            .ToList();
        var page = SupervisorBlocks.Order(rows, r => r.BlocksIn, r => r.NeededOn, r => r.OrderNumber).Take(SupervisorBlocks.ShortageRows).ToList();

        var pageOrders = page.Select(r => r.OrderId).Distinct().ToList();
        var requestLines = (await requests.ForOrdersAsync(pageOrders, cancellationToken)).ToLookup(l => (l.ProductionOrderId, l.ItemId));
        var requestLineIds = requestLines.SelectMany(g => g).Where(l => l.Status == PurchaseRequestStatus.Ordered).Select(l => l.LineId).Distinct().ToList();
        var pos = await purchaseOrders.ForRequestLinesAsync(requestLineIds, cancellationToken);
        var itemNames = (await items.FindAsync(page.Select(r => r.ItemId).Distinct().ToList(), cancellationToken)).ToDictionary(i => i.Id);
        var projectNumbers = (await projects.FindManyAsync(page.Select(r => r.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id, p => p.Number);
        var list = page.Select(r =>
        {
            var purchase = SupervisorBlocks.Purchase([.. requestLines[(r.OrderId, r.ItemId)]], pos);
            var item = itemNames.GetValueOrDefault(r.ItemId);
            return new SupervisorShortageItem(
                r.OrderId, r.OrderNumber, r.ProjectId, projectNumbers.GetValueOrDefault(r.ProjectId, string.Empty), r.ItemId, item?.Code ?? string.Empty, item?.Name ?? "Item removido",
                item?.UnitCode ?? string.Empty, r.ShortQty, r.NeededOn, SupervisorBlocks.Blocks(r.BlocksIn), r.BlocksIn, purchase.Situation, purchase.Label, purchase.RequestId,
                purchase.OrderId);
        }).ToList();
        var canCreate = await supervisor.CanAsync(SupervisorBlocks.CreateRequestPermission, cancellationToken);
        return new SupervisorShortagesBlock(rows.Count, rows.Count(r => SupervisorBlocks.Blocks(r.BlocksIn)), list, canCreate, clock.GetUtcNow());
    }
}
