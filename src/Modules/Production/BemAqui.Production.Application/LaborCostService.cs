using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.HR.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Custo de mão de obra da OS (PRD-04 CA 5, 6, 14 e 17): aba Custos e coluna Custo dos apontamentos e o histórico do custo (com
/// <c>projects.view_cost</c>, no endpoint), e o "Recalcular apontamentos sem custo" (com <c>production.approve</c>). OS e apontamento
/// conferidos pelo id da OS (IDOR).
/// </summary>
public sealed class LaborCostService(
    IProductionDb db, LaborCosting costing, IEventPublisher events, IPositions positions, IUserDirectory users, IPermissionChecker permissions, ProductionParametersService parameters,
    TimeProvider clock, BemAqui.Inventory.Contracts.IStockSerials serials, FinishedGoods finishedGoods)
{
    /// <summary>Custo por apontamento e por etapa, total de mão de obra e os cargos sem custo-hora (CA 14).</summary>
    public async Task<Result<OrderLaborCostView>> OrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId && o.Type == ProductionOrderType.Fabrication, cancellationToken);
        if (order is null)
        {
            return InstallationOrderService.NotFound();
        }

        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == orderId).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var entries = await db.LaborEntries.AsNoTracking().Where(e => e.OrderId == orderId).ToListAsync(cancellationToken);
        var ids = entries.Select(e => e.Id).ToList();
        var pauses = (await db.StepPauses.AsNoTracking().Where(p => ids.Contains(p.LaborEntryId)).Select(p => new { p.LaborEntryId, p.StartedAt, p.EndedAt }).ToListAsync(cancellationToken))
            .ToLookup(p => p.LaborEntryId, p => new PauseSpan(p.StartedAt, p.EndedAt));
        var now = clock.GetUtcNow();
        var minutes = entries.ToDictionary(e => e.Id, e => LaborRules.NetMinutes(e.StartedAt, e.EndedAt, pauses[e.Id], now));
        // Decisão do PO (22/10/2026): custo-hora e custo por apontamento só com o parâmetro ligado; desligado, só os totais.
        var showEntries = await parameters.ShowLaborEntryCostAsync(cancellationToken);
        var views = !showEntries ? [] : entries.OrderByDescending(e => e.StartedAt)
            .Select(e => new LaborEntryCostView(e.Id, e.HourlyCostSnapshot, e.Cost, e.CostMissing, e.CostBackfilled, e.CostVersion, e.EndedAt is null || e.CostVersion == 0)).ToList();
        var byStep = entries.ToLookup(e => e.OrderStepId);
        var stepViews = steps.Where(s => byStep[s.Id].Any()).Select(s =>
        {
            var mine = byStep[s.Id].ToList();
            var stepMinutes = mine.Sum(e => minutes[e.Id]);
            return new StepLaborCostView(s.Id, s.Code ?? string.Empty, s.Name, stepMinutes, LaborRules.Hours(stepMinutes), mine.Sum(e => e.Cost), mine.Count(e => e.CostMissing), mine.Any(e => e.CostBackfilled));
        }).ToList();
        var total = entries.Sum(e => minutes[e.Id]);
        // PRD-06a2 CA 11: a entrada do produto acabado (custo unitário e total), lida do Inventory; só existe com o Estoque liberado.
        var entry = finishedGoods.Enabled ? await serials.EntryAsync(orderId, cancellationToken) : null;
        return new OrderLaborCostView(
            order.Id, order.Number, showEntries, views, stepViews, LaborRules.Hours(total), entries.Sum(e => e.Cost), entries.Count(e => e.CostMissing), entries.Any(e => e.CostBackfilled),
            await MissingPositionsAsync(entries.Where(e => e.CostMissing).ToList(), cancellationToken),
            entry is null ? null : new FinishedGoodsCostView(entry.MovementId, entry.Number, entry.Quantity, entry.UnitCost, entry.TotalCost, entry.PostedAt, entry.Reversed));
    }

    /// <summary>Histórico do custo do apontamento (CA 17), da versão mais nova para a mais antiga.</summary>
    public async Task<Result<IReadOnlyList<LaborCostHistoryView>>> HistoryAsync(Guid orderId, Guid entryId, CancellationToken cancellationToken = default)
    {
        if (!await db.LaborEntries.AnyAsync(e => e.Id == entryId && e.OrderId == orderId, cancellationToken))
        {
            return Error.NotFound("labor_entry_not_found", "Apontamento não encontrado nesta OS.");
        }

        if (!await parameters.ShowLaborEntryCostAsync(cancellationToken))
        {
            return Error.Forbidden("entry_cost_hidden", "O custo de cada apontamento não é exibido. Ligue \"Exibir custo-hora em cada apontamento\" em Configurações → Parâmetros.");
        }

        var rows = await db.LaborCostHistory.AsNoTracking().Where(h => h.LaborEntryId == entryId).OrderByDescending(h => h.CostVersion).ToListAsync(cancellationToken);
        var names = new Dictionary<Guid, string?>();
        foreach (var user in rows.Where(r => r.RecordedBy is not null).Select(r => r.RecordedBy!.Value).Distinct())
        {
            names[user] = await users.DisplayNameAsync(user, cancellationToken);
        }

        return rows.Select(r => new LaborCostHistoryView(
            r.CostVersion, r.Origin, r.Minutes, LaborRules.Hours(r.Minutes), r.HourlyCostSnapshot, r.PreviousAmount, r.Amount, r.CostMissing,
            r.RecordedBy is { } by ? names.GetValueOrDefault(by) : null, r.RecordedAt)).ToList();
    }

    /// <summary>Impacto do recálculo (CA 6): apontamentos "Sem custo-hora" do filtro e o total novo (só com <c>projects.view_cost</c>).</summary>
    public async Task<LaborCostMissingView> MissingAsync(Guid? positionId, Guid? orderId, CancellationToken cancellationToken = default)
    {
        var missing = await Missing(positionId, orderId).AsNoTracking().ToListAsync(cancellationToken);
        var plan = await PlanAsync(missing, cancellationToken);
        var total = await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken) ? plan.Sum(p => p.Cost) : (decimal?)null;
        return new LaborCostMissingView(missing.Count, plan.Count, total, await MissingPositionsAsync(missing, cancellationToken));
    }

    /// <summary>
    /// "Recalcular apontamentos sem custo" (CA 6): só os marcados <c>cost_missing</c> cujo cargo já tem custo-hora recebem o custo-hora
    /// atual como snapshot, perdem a marca, sobem a versão e publicam <c>LaborEntryCorrected</c> (<c>Recalculation</c>) com a
    /// diferença. Apontamento com snapshot nunca é recalculado.
    /// </summary>
    public async Task<Result<LaborRecalculationResult>> RecalculateAsync(LaborRecalculationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var missing = await Missing(input.PositionId, input.OrderId).ToListAsync(cancellationToken);
        var plan = await PlanAsync(missing, cancellationToken);
        if (plan.Count == 0)
        {
            return Error.Conflict("labor_no_rate", missing.Count == 0
                ? "Não há apontamento sem custo para recalcular."
                : "Defina o custo-hora do cargo antes de recalcular os apontamentos sem custo.");
        }

        var projects = await db.ProductionOrders.AsNoTracking().Where(o => plan.Select(p => p.Entry.OrderId).Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.ProjectId, cancellationToken);
        foreach (var (entry, minutes, position, rate, _) in plan)
        {
            events.Publish(costing.Recalculate(entry, projects[entry.OrderId], minutes, position, rate));
        }

        await db.SaveChangesAsync(cancellationToken);
        var canSee = await permissions.HasPermissionAsync("projects", "view_cost", cancellationToken);
        return new LaborRecalculationResult(plan.Count, canSee ? plan.Sum(p => p.Cost) : null);
    }

    private IQueryable<LaborEntry> Missing(Guid? positionId, Guid? orderId) =>
        db.LaborEntries.Where(e => e.CostMissing && e.EndedAt != null && e.CostVersion > 0 && (positionId == null || e.CostPositionId == positionId) && (orderId == null || e.OrderId == orderId));

    private async Task<List<(LaborEntry Entry, int Minutes, Guid Position, decimal Rate, decimal Cost)>> PlanAsync(List<LaborEntry> missing, CancellationToken ct)
    {
        var ids = missing.Select(e => e.Id).ToList();
        var pauses = (await db.StepPauses.AsNoTracking().Where(p => ids.Contains(p.LaborEntryId)).Select(p => new { p.LaborEntryId, p.StartedAt, p.EndedAt }).ToListAsync(ct))
            .ToLookup(p => p.LaborEntryId, p => new PauseSpan(p.StartedAt, p.EndedAt));
        var plan = new List<(LaborEntry, int, Guid, decimal, decimal)>();
        foreach (var entry in missing)
        {
            var position = entry.CostPositionId ?? (await costing.CurrentRateAsync(entry.EmployeeId, ct)).PositionId;
            if (position is not { } id || await costing.RateAsync(id, ct) is not { } rate || !LaborCostCalculator.HasRate(rate))
            {
                continue;
            }

            var minutes = LaborRules.NetMinutes(entry.StartedAt, entry.EndedAt, pauses[entry.Id], entry.EndedAt!.Value);
            plan.Add((entry, minutes, id, rate, LaborCostCalculator.Cost(minutes, rate)));
        }

        return plan;
    }

    private async Task<IReadOnlyList<MissingCostPosition>> MissingPositionsAsync(List<LaborEntry> missing, CancellationToken ct)
    {
        if (missing.Count == 0)
        {
            return [];
        }

        var groups = missing.GroupBy(e => e.CostPositionId).ToList();
        var names = (await positions.FindAsync(groups.Where(g => g.Key is not null).Select(g => g.Key!.Value).ToList(), ct)).ToDictionary(p => p.Id, p => p.Name);
        var result = new List<MissingCostPosition>();
        foreach (var g in groups)
        {
            var rate = g.Key is { } id ? await costing.RateAsync(id, ct) : null;
            result.Add(new MissingCostPosition(g.Key, g.Key is { } key ? names.GetValueOrDefault(key, "Cargo") : "Sem cargo", g.Count(), LaborCostCalculator.HasRate(rate)));
        }

        return result.OrderBy(r => r.PositionName, StringComparer.CurrentCulture).ToList();
    }
}