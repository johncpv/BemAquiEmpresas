using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// <see cref="IProductionBoard"/> (DSH-03 CA 2): o quadro do Kanban (<see cref="ProductionBoardService"/>, PRD-02) resumido por coluna, só com
/// os cartões atrasados e em risco; a linha da OS é a do centro da etapa atual, como no filtro do Kanban.
/// </summary>
public sealed class ProductionBoardContract(ProductionBoardService board, IProductionDb db) : IProductionBoard
{
    public async Task<ProductionBoardSummary> SummaryAsync(string? line, CancellationToken cancellationToken = default)
    {
        var data = await board.GetAsync(new ProductionBoardQuery(null, null, Normalize(line), null, false, false), cancellationToken);
        var columns = data.Columns.Select(c =>
        {
            var mine = data.Cards.Where(card => card.ColumnKey == c.Key).ToList();
            return new BoardColumnSummary(c.Key, c.Code, c.Name, c.Count, c.RemainingHours, mine.Count(x => x.Risk == DeliveryRisk.Late), mine.Count(x => x.Risk == DeliveryRisk.AtRisk));
        }).ToList();
        // Atrasadas primeiro (a mais atrasada antes), depois as em risco pela data prometida (CA 2: o que pede ação vem primeiro).
        var highlighted = data.Cards.Where(c => c.Risk != DeliveryRisk.None)
            .OrderBy(c => c.Risk == DeliveryRisk.Late ? 0 : 1).ThenByDescending(c => c.DelayDays ?? 0).ThenBy(c => c.PromisedDate).ThenBy(c => c.Number, StringComparer.Ordinal)
            .Select(c => new BoardHighlightCard(
                c.Id, c.Number, c.ProductName, c.ProjectId, c.ProjectNumber, c.PartyName, c.ColumnKey, c.CurrentStepCode, c.CurrentStepName, c.Codes, c.StepNames, c.CurrentStepIndex,
                c.PromisedDate, c.Risk, c.DelayDays))
            .ToList();
        return new ProductionBoardSummary(columns, highlighted, data.LateCount, data.AtRiskCount, data.Cards.Count);
    }

    public async Task<IReadOnlyDictionary<Guid, string?>> LinesOfOrdersAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        if (orderIds.Count == 0)
        {
            return new Dictionary<Guid, string?>();
        }

        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => orderIds.Contains(s.OrderId))
            .Select(s => new { s.OrderId, s.Sequence, s.Status, s.WorkCenterId }).ToListAsync(cancellationToken);
        var centerIds = steps.Where(s => s.WorkCenterId != null).Select(s => s.WorkCenterId!.Value).Distinct().ToList();
        var lines = await db.WorkCenters.AsNoTracking().Where(c => centerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Line, cancellationToken);
        return steps.GroupBy(s => s.OrderId).ToDictionary(g => g.Key, g =>
        {
            var ordered = g.OrderBy(s => s.Sequence).ToList();
            var current = ordered.FirstOrDefault(s => s.Status != ProductionStepStatus.Done) ?? ordered[^1];
            return current.WorkCenterId is { } center ? lines.GetValueOrDefault(center) : null;
        });
    }

    internal static string? Normalize(string? line) => string.IsNullOrWhiteSpace(line) ? null : line.Trim();
}

/// <summary>
/// <see cref="IQualityOverview"/> (DSH-03 CA 4): a fila "aguardando" da PRD-05 (<see cref="QualityService.QueueAsync"/>, com a segregação do item 76
/// e a "Autoinspeção") e as NC não encerradas, filtradas pela linha do centro da etapa de qualidade.
/// </summary>
public sealed class QualityOverviewContract(QualityService quality, IProductionDb db, IUserDirectory users, TimeProvider clock) : IQualityOverview
{
    public async Task<QualityPendingSummary> PendingAsync(string? line, bool withItems, CancellationToken cancellationToken = default)
    {
        var filter = ProductionBoardContract.Normalize(line);
        var pending = await (from s in db.ProductionOrderSteps.AsNoTracking()
                             join o in db.ProductionOrders.AsNoTracking() on s.OrderId equals o.Id
                             join c in db.WorkCenters.AsNoTracking() on s.WorkCenterId equals (Guid?)c.Id into centers
                             from c in centers.DefaultIfEmpty()
                             where o.Type == ProductionOrderType.Fabrication && o.Status != ProductionOrderStatus.Cancelled && o.Status != ProductionOrderStatus.Completed
                                && s.IsQualityStep && (s.Status == ProductionStepStatus.Ready || s.Status == ProductionStepStatus.InProgress || s.Status == ProductionStepStatus.Paused)
                                && (filter == null || (c != null && c.Line == filter))
                             select s.Id).ToListAsync(cancellationToken);
        if (!withItems || pending.Count == 0)
        {
            return new QualityPendingSummary([], pending.Count);
        }

        var ids = pending.ToHashSet();
        var queue = await quality.QueueAsync("todas", cancellationToken);
        var decidable = queue.Items.Where(i => i.CanDecide && ids.Contains(i.StepId))
            .Select(i => new QualityPendingStep(i.StepId, i.OrderId, i.OrderNumber, i.StepCode, i.StepName, i.ProductName, i.ProjectNumber, i.PromisedDate, i.Attempt, i.WaitingSince,
                i.SelfInspection, i.OpenNonConformities))
            .ToList();
        return new QualityPendingSummary(decidable, pending.Count);
    }

    public async Task<OpenNonConformities> OpenNonConformitiesAsync(string? line, int limit, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var filter = ProductionBoardContract.Normalize(line);
        var rows = from n in db.NonConformities.AsNoTracking()
                   join o in db.ProductionOrders.AsNoTracking() on n.OrderId equals o.Id
                   join s in db.ProductionOrderSteps.AsNoTracking() on n.OrderStepId equals s.Id
                   join c in db.WorkCenters.AsNoTracking() on s.WorkCenterId equals (Guid?)c.Id into centers
                   from c in centers.DefaultIfEmpty()
                   where n.Status != NonConformityStatus.Closed && (filter == null || (c != null && c.Line == filter))
                   select new { n.Id, n.Number, n.OrderId, OrderNumber = o.Number, s.Code, s.Name, n.ResponsibleUserId, n.Status, n.OpenedAt };
        var total = await rows.CountAsync(cancellationToken);
        var page = await rows.OrderBy(r => r.OpenedAt).ThenBy(r => r.Number).Take(limit).ToListAsync(cancellationToken);
        var names = new Dictionary<Guid, string?>();
        foreach (var id in page.Select(r => r.ResponsibleUserId).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, cancellationToken);
        }

        var today = BusinessTime.Today(clock);
        return new OpenNonConformities(page.Select(r => new OpenNonConformity(
            r.Id, r.Number, r.OrderId, r.OrderNumber, r.Code ?? string.Empty, r.Name, r.ResponsibleUserId, names.GetValueOrDefault(r.ResponsibleUserId) ?? "Usuário",
            r.Status == NonConformityStatus.InAction ? NonConformityState.InAction : NonConformityState.Open,
            QualityRules.AgeDays(DateOnly.FromDateTime(BusinessTime.ToBusiness(r.OpenedAt).DateTime), null, today), r.OpenedAt)).ToList(), total);
    }
}

/// <summary>
/// <see cref="IProductionIndicators"/> (DSH-03 CA 6): horas paradas por motivo (<c>production.step_pauses</c>, PRD-03a) e taxa de NC por etapa de
/// qualidade (<c>production.quality_decisions</c>, PRD-05), uma consulta cada, com a soma no processo pela regra <see cref="ProductionIndicatorRules"/>.
/// </summary>
public sealed class ProductionIndicatorsContract(IProductionDb db, TimeProvider clock) : IProductionIndicators
{
    /// <summary>OS listadas por motivo no drill-down.</summary>
    public const int OrdersPerReason = 5;

    private static readonly PauseReason[] Stoppages = [PauseReason.Material, PauseReason.Machine, PauseReason.Other];

    public async Task<ProductionIndicatorsSummary> SummaryAsync(DateOnly from, DateOnly to, string? line, CancellationToken cancellationToken = default)
    {
        var filter = ProductionBoardContract.Normalize(line);
        var now = clock.GetUtcNow();
        var start = BusinessTime.StartOfDayUtc(from);
        var end = BusinessTime.StartOfDayUtc(to.AddDays(1));
        if (end > now)
        {
            end = now;
        }

        var pauses = await (from p in db.StepPauses.AsNoTracking()
                            join e in db.LaborEntries.AsNoTracking() on p.LaborEntryId equals e.Id
                            join s in db.ProductionOrderSteps.AsNoTracking() on e.OrderStepId equals s.Id
                            join o in db.ProductionOrders.AsNoTracking() on p.OrderId equals o.Id
                            join c in db.WorkCenters.AsNoTracking() on s.WorkCenterId equals (Guid?)c.Id into centers
                            from c in centers.DefaultIfEmpty()
                            where Stoppages.Contains(p.Reason) && p.StartedAt < end && (p.EndedAt == null || p.EndedAt > start) && (filter == null || (c != null && c.Line == filter))
                            select new { p.Reason, p.StartedAt, p.EndedAt, o.Id, o.Number }).ToListAsync(cancellationToken);
        var stoppages = Stoppages.Select(reason =>
        {
            var mine = pauses.Where(p => p.Reason == reason)
                .Select(p => (p.Id, p.Number, Minutes: ProductionIndicatorRules.MinutesWithin(p.StartedAt, p.EndedAt, now, start, end))).Where(p => p.Minutes > 0).ToList();
            var orders = mine.GroupBy(p => (p.Id, p.Number)).Select(g => new StoppageOrderHours(g.Key.Id, g.Key.Number, ProductionIndicatorRules.Hours(g.Sum(x => x.Minutes))))
                .OrderByDescending(o => o.Hours).ThenBy(o => o.OrderNumber, StringComparer.Ordinal).Take(OrdersPerReason).ToList();
            return new StoppageByReason(ProductionIndicatorRules.StoppageOf(reason)!.Value, ProductionIndicatorRules.Hours(mine.Sum(p => p.Minutes)), mine.Count, orders);
        }).ToList();

        var decisions = await (from d in db.QualityDecisions.AsNoTracking()
                               join s in db.ProductionOrderSteps.AsNoTracking() on d.StepId equals s.Id
                               join c in db.WorkCenters.AsNoTracking() on s.WorkCenterId equals (Guid?)c.Id into centers
                               from c in centers.DefaultIfEmpty()
                               where d.DecidedAt >= start && d.DecidedAt < end && (filter == null || (c != null && c.Line == filter))
                               select new { s.Code, s.Name, d.Result }).ToListAsync(cancellationToken);
        var rates = decisions.GroupBy(d => string.IsNullOrWhiteSpace(d.Code) ? "?" : d.Code.Trim().ToUpperInvariant(), StringComparer.Ordinal)
            .Select(g =>
            {
                var approvals = g.Count(d => d.Result == QualityResult.Approved);
                var rejections = g.Count(d => d.Result == QualityResult.Rejected);
                var name = g.GroupBy(d => d.Name, StringComparer.Ordinal).OrderByDescending(n => n.Count()).ThenBy(n => n.Key, StringComparer.Ordinal).First().Key;
                return new QualityRateByStep(g.Key, name, approvals + rejections, rejections, ProductionIndicatorRules.NcRate(approvals, rejections));
            })
            .OrderByDescending(r => r.RatePercent ?? 0).ThenBy(r => r.StepCode, StringComparer.Ordinal).ToList();
        return new ProductionIndicatorsSummary(from, to, stoppages, rates);
    }
}
