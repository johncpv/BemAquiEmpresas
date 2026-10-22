using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Engineering.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Kanban das OS de fabricação (PRD-02 CA 1 a 6, 14): as OS abertas (<c>Open</c>, <c>InProgress</c>, <c>Paused</c>, <c>Quality</c>)
/// numa consulta, as etapas em outra e os nomes de projeto, cliente, produto, centro e roteiro de uma vez por contrato (sem N+1).
/// Colunas pelo código da etapa (D10); atraso e risco pela função única <see cref="ProductionOrderRisk"/>.
/// </summary>
public sealed class ProductionBoardService(
    IProductionDb db, IProjects projects, IParties parties, IItems items, IWorkCenters workCenters, IRoutingTemplates routings, BemAqui.HR.Contracts.IEmployees employees,
    TimeProvider clock)
{
    private static readonly ProductionOrderStatus[] OpenStatuses =
        [ProductionOrderStatus.Open, ProductionOrderStatus.InProgress, ProductionOrderStatus.Paused, ProductionOrderStatus.Quality];

    public async Task<ProductionBoard> GetAsync(ProductionBoardQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var orders = await db.ProductionOrders.AsNoTracking()
            .Where(o => o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status))
            .OrderBy(o => o.Priority).ThenBy(o => o.CreatedAt).ThenBy(o => o.Id)
            .Select(o => new { o.Id, o.Number, o.Status, o.ProjectId, o.ProjectItemId, o.ProductId, o.Quantity, o.RoutingTemplateId, o.Version, o.SelfInspection })
            .ToListAsync(cancellationToken);
        var ids = orders.Select(o => o.Id).ToList();
        // PRD-05 CA 11: chip "NC aberta" no cartão (NC não encerrada da OS).
        var openNc = (await db.NonConformities.AsNoTracking().Where(n => ids.Contains(n.OrderId) && n.Status != NonConformityStatus.Closed).GroupBy(n => n.OrderId)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken)).ToDictionary(g => g.Key, g => g.Count);
        var steps = (await db.ProductionOrderSteps.AsNoTracking().Where(s => ids.Contains(s.OrderId)).OrderBy(s => s.Sequence)
                .Select(s => new { s.OrderId, s.Id, s.Sequence, s.Code, s.Name, s.WorkCenterId, s.StandardHours, s.Status, s.IsQualityStep })
                .ToListAsync(cancellationToken))
            .ToLookup(s => s.OrderId);

        var byProject = (await projects.FindManyAsync(orders.Select(o => o.ProjectId).Distinct().ToList(), cancellationToken)).ToDictionary(p => p.Id);
        var partyNames = (await parties.FindManyAsync(byProject.Values.Select(p => p.PartyId).Distinct().ToList(), cancellationToken))
            .ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var products = (await items.FindAsync(orders.Where(o => o.ProductId is not null).Select(o => o.ProductId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(i => i.Id, i => i.Name);
        var projectItems = (await projects.FindItemsAsync(orders.Where(o => o.ProjectItemId is not null).Select(o => o.ProjectItemId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(i => i.Id, i => i.Description);
        var centers = (await workCenters.FindAsync(steps.SelectMany(g => g).Where(s => s.WorkCenterId is not null).Select(s => s.WorkCenterId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.Id);
        var routingNames = (await routings.FindAsync(orders.Where(o => o.RoutingTemplateId is not null).Select(o => o.RoutingTemplateId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(r => r.Id, r => r.Name);
        var today = BusinessTime.Today(clock);

        // Apontamento (PRD-03a): horas apontadas da etapa atual, quem está nela (iniciais) e a pausa.
        var currentIds = steps.Select(g => g.ToList()).Select(m => m.FirstOrDefault(s => s.Status != ProductionStepStatus.Done)?.Id).Where(id => id is not null).Select(id => id!.Value).ToList();
        var applied = await LaborReader.AppliedMinutesAsync(db, currentIds, clock.GetUtcNow(), cancellationToken);
        var open = await LaborReader.OpenByStepAsync(db, currentIds, cancellationToken);
        var people = (await employees.FindAsync(open.SelectMany(g => g).Select(e => e.EmployeeId).Distinct().ToList(), cancellationToken)).ToDictionary(e => e.Id, e => e.Name);

        var cards = orders.Select(o =>
        {
            var mine = steps[o.Id].ToList();
            var boardSteps = mine.Select(s => new BoardStep(s.Code, s.Name, s.Sequence, s.Status)).ToList();
            var currentIndex = BoardRules.CurrentIndex(boardSteps);
            var current = currentIndex >= 0 ? mine[currentIndex] : null;
            // Linha: centro da etapa atual; na "Pronta para concluir", o da última etapa (a OS continua na linha dela).
            var lineStep = current ?? (mine.Count > 0 ? mine[^1] : null);
            var project = byProject.GetValueOrDefault(o.ProjectId);
            var statuses = mine.Select(s => s.Status).ToList();
            var risk = ProductionOrderRisk.Evaluate(project?.PromisedDate, today, o.Status, ProductionOrderRisk.IsAtLastStep(statuses));
            var product = o.ProductId is { } p ? products.GetValueOrDefault(p) : null;
            var onStep = current is null ? [] : open[current.Id].ToList();
            var working = onStep.Any(e => e.Paused is null);
            var pausedReason = onStep.Count > 0 && !working ? ShopfloorService.PauseLabel(onStep[0].Paused!.Value) : null;
            var initials = onStep.Count == 0 ? null : string.Join(" ", onStep.Select(e => Initials(people.GetValueOrDefault(e.EmployeeId, "?"))).Distinct());
            return new BoardCard(
                o.Id, o.Number, o.Status, product ?? (o.ProjectItemId is { } item ? projectItems.GetValueOrDefault(item, string.Empty) : string.Empty), o.Quantity.GetValueOrDefault(),
                o.ProjectId, project?.Number ?? string.Empty, project?.Name ?? string.Empty, project?.PartyId ?? Guid.Empty,
                project is null ? string.Empty : partyNames.GetValueOrDefault(project.PartyId, string.Empty), BoardRules.ColumnKey(boardSteps),
                current?.Id, current?.Code, current?.Name, current?.Status, current?.IsQualityStep == true, current?.StandardHours ?? 0m,
                current is null ? 0m : LaborRules.Hours(applied.GetValueOrDefault(current.Id)),
                lineStep?.WorkCenterId is { } center ? centers.GetValueOrDefault(center)?.Line : null, mine.Select(s => s.Code ?? string.Empty).ToList(),
                mine.Select(s => s.Name).ToList(), mine.Count == 0 ? -1 : currentIndex < 0 ? mine.Count - 1 : currentIndex, project?.PromisedDate, risk.Risk,
                risk.DelayDays, mine.Count > 0 && currentIndex < 0, mine.Count == 0, current?.Status == ProductionStepStatus.InProgress && working, pausedReason, initials,
                o.RoutingTemplateId, o.Version, openNc.GetValueOrDefault(o.Id), o.SelfInspection);
        }).ToList();

        // Opções dos filtros: das OS abertas, antes de filtrar.
        var projectOptions = cards.GroupBy(c => c.ProjectId).Select(g => new BoardOption(g.Key, g.First().ProjectNumber + " · " + g.First().ProjectName))
            .OrderBy(o => o.Label, StringComparer.CurrentCulture).ToList();
        var partyOptions = cards.Where(c => c.PartyId != Guid.Empty).GroupBy(c => c.PartyId).Select(g => new BoardOption(g.Key, g.First().PartyName))
            .OrderBy(o => o.Label, StringComparer.CurrentCulture).ToList();
        var routingOptions = orders.Where(o => o.RoutingTemplateId is not null).Select(o => o.RoutingTemplateId!.Value).Distinct()
            .Select(id => new BoardOption(id, routingNames.GetValueOrDefault(id, "Roteiro excluído"))).OrderBy(o => o.Label, StringComparer.CurrentCulture).ToList();
        var lines = cards.Where(c => !string.IsNullOrWhiteSpace(c.Line)).Select(c => c.Line!).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.CurrentCulture).ToList();

        // O roteiro escolhe as colunas (CA 2); os demais filtros só os cartões.
        var inRouting = query.RoutingTemplateId is { } routing ? cards.Where(c => c.RoutingTemplateId == routing).ToList() : cards;
        var line = query.Line?.Trim();
        var filtered = inRouting
            .Where(c => query.ProjectId is not { } projectId || c.ProjectId == projectId)
            .Where(c => query.PartyId is not { } partyId || c.PartyId == partyId)
            .Where(c => string.IsNullOrEmpty(line) || string.Equals(c.Line, line, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var lateCount = filtered.Count(c => c.Risk == DeliveryRisk.Late);
        var riskCount = filtered.Count(c => c.Risk == DeliveryRisk.AtRisk);
        if (query.Late || query.AtRisk)
        {
            filtered = filtered.Where(c => (query.Late && c.Risk == DeliveryRisk.Late) || (query.AtRisk && c.Risk == DeliveryRisk.AtRisk)).ToList();
        }

        var columnSource = inRouting.Select(c => steps[c.Id].Select(s => new BoardStep(s.Code, s.Name, s.Sequence, s.Status)).ToList());
        var columns = new List<BoardColumn>();
        if (inRouting.Any(c => c.NoRouting))
        {
            columns.Add(Column(BoardRules.NoRoutingKey, BoardColumnKind.NoRouting, null, BoardRules.NoRoutingName, filtered));
        }

        columns.AddRange(BoardRules.StepColumns(columnSource).Select(c => Column(c.Code, BoardColumnKind.Step, c.Code, c.Name, filtered)));
        columns.Add(Column(BoardRules.ReadyKey, BoardColumnKind.ReadyToComplete, null, BoardRules.ReadyName, filtered));
        return new ProductionBoard(columns, filtered, lateCount, riskCount, cards.Count, projectOptions, partyOptions, routingOptions, lines);
    }

    /// <summary>Iniciais do funcionário no cartão (CA 3 da PRD-02): primeira letra do primeiro e do último nome.</summary>
    public static string Initials(string name)
    {
        var parts = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    private static BoardColumn Column(string key, BoardColumnKind kind, string? code, string name, IReadOnlyList<BoardCard> cards)
    {
        var mine = cards.Where(c => c.ColumnKey == key).ToList();
        return new BoardColumn(key, kind, code, name, mine.Count, mine.Sum(c => BoardRules.Remaining(c.StandardHours, c.AppliedHours)));
    }
}
