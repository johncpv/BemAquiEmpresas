using BemAqui.BuildingBlocks.Agenda;
using BemAqui.Commercial.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// Detector da Produção para a Minha pauta (DSH-04 CA 2 e 13): qualidade para decidir (a fila "aguardando" da PRD-05, com a segregação do
/// item 76 e a marca "Autoinspeção"), OS atrasadas (prometido do projeto antes de hoje) e as NC sob a responsabilidade da pessoa.
/// </summary>
public sealed class ProductionAgendaSource(IProductionDb db, QualityService quality, IProjects projects) : IAgendaSource
{
    public const string QualityPending = "production.quality_pending";
    public const string OrdersLate = "production.orders_late";
    public const string NonConformitiesMine = "production.non_conformities_mine";
    public const string SelfInspectionBadge = "Autoinspeção";

    private static readonly AgendaCategory[] Declared =
    [
        new(QualityPending, "production", QualityService.Permission, AgendaTier.Unblocks, "a qualidade para decidir"),
        new(OrdersLate, "production", "production.approve", AgendaTier.Overdue, "as OS atrasadas"),
        // A NC é de quem é responsável por ela; ver a NC exige production.view (nenhum item de documento que a pessoa não abre, CA 13).
        new(NonConformitiesMine, "production", "production.view", AgendaTier.Overdue, "as não conformidades sob sua responsabilidade"),
    ];

    private static readonly ProductionOrderStatus[] OpenStatuses =
        [ProductionOrderStatus.Open, ProductionOrderStatus.InProgress, ProductionOrderStatus.Paused, ProductionOrderStatus.Quality, ProductionOrderStatus.ReadyToComplete];

    public string Module => "production";

    public IReadOnlyList<AgendaCategory> Categories => Declared;

    public async Task<IReadOnlyList<AgendaItem>> ItemsAsync(AgendaRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var items = new List<AgendaItem?>();
        if (request.Wants(QualityPending))
        {
            items.Add(await QualityAsync(request, cancellationToken));
        }

        if (request.Wants(OrdersLate))
        {
            items.Add(await LateAsync(request, cancellationToken));
        }

        if (request.Wants(NonConformitiesMine))
        {
            items.Add(await NonConformitiesAsync(request, cancellationToken));
        }

        return [.. items.OfType<AgendaItem>()];
    }

    /// <summary>A mesma fila "aguardando" da qualidade (CanDecide): quem apontou etapa da tentativa não vê, salvo a Autoinspeção.</summary>
    private async Task<AgendaItem?> QualityAsync(AgendaRequest request, CancellationToken ct)
    {
        var mine = (await quality.QueueAsync(null, ct)).Items.Where(i => i.CanDecide).ToList();
        if (mine.Count == 0)
        {
            return null;
        }

        var first = mine.Where(i => i.PromisedDate is not null).Select(i => i.PromisedDate!.Value).DefaultIfEmpty().Min();
        DateOnly? due = first == default ? null : first;
        var self = mine.Count(i => i.SelfInspection);
        return new AgendaItem(
            QualityPending,
            AgendaText.Count(mine.Count, "OS aguarda sua decisão de qualidade", "OS aguardam sua decisão de qualidade"),
            AgendaText.Sentence(
                due is { } promised ? (promised < request.Today ? "a primeira estava prometida para " + AgendaText.DayMonth(promised, request.Today) : "a primeira está prometida para " + AgendaText.Day(promised, request.Today)) : "sem prazo prometido",
                self == 0 ? null : self == 1 ? "1 seria Autoinspeção" : $"{self} seriam Autoinspeção"),
            "/app/producao/qualidade", due, null, AgendaTier.Unblocks, mine.Count, "Decidir qualidade",
            [.. mine.OrderBy(i => i.PromisedDate ?? DateOnly.MaxValue).ThenBy(i => i.WaitingSince).Take(AgendaLimits.EntriesPerCategory).Select(i => new AgendaEntry(
                $"{i.OrderNumber} · {i.StepName}", $"{i.ProductName} · {i.ProjectNumber}", "/app/producao/qualidade/" + i.StepId, i.PromisedDate, null,
                i.SelfInspection ? SelfInspectionBadge : null))]);
    }

    private async Task<AgendaItem?> LateAsync(AgendaRequest request, CancellationToken ct)
    {
        var orders = await db.ProductionOrders.AsNoTracking()
            .Where(o => o.Type == ProductionOrderType.Fabrication && OpenStatuses.Contains(o.Status))
            .Select(o => new { o.Id, o.Number, o.ProjectId })
            .ToListAsync(ct);
        if (orders.Count == 0)
        {
            return null;
        }

        var byProject = (await projects.FindManyAsync(orders.Select(o => o.ProjectId).Distinct().ToList(), ct)).ToDictionary(p => p.Id);
        var late = orders
            .Select(o => (Order: o, Project: byProject.GetValueOrDefault(o.ProjectId)))
            .Where(x => x.Project?.PromisedDate is { } promised && promised < request.Today)
            .Select(x => (x.Order, Project: x.Project!, Promised: x.Project!.PromisedDate!.Value))
            .OrderBy(x => x.Promised).ThenBy(x => x.Order.Number, StringComparer.Ordinal)
            .ToList();
        if (late.Count == 0)
        {
            return null;
        }

        var oldest = late[0].Promised;
        var days = request.Today.DayNumber - oldest.DayNumber;
        return new AgendaItem(
            OrdersLate,
            AgendaText.Count(late.Count, "OS está atrasada", "OS estão atrasadas"),
            AgendaText.Sentence($"a mais atrasada estava prometida para {AgendaText.DayMonth(oldest, request.Today)} ({days} {(days == 1 ? "dia" : "dias")} de atraso)"),
            "/app/producao/os?atraso=1", oldest, null, AgendaTier.Overdue, late.Count, "Abrir OS",
            [.. late.Take(AgendaLimits.EntriesPerCategory).Select(x => new AgendaEntry(
                x.Order.Number, $"{x.Project.Number} · prometido {AgendaText.DayMonth(x.Promised, request.Today)}", FabricationOrderService.Link + x.Order.Id, x.Promised, null))]);
    }

    private async Task<AgendaItem?> NonConformitiesAsync(AgendaRequest request, CancellationToken ct)
    {
        var me = request.UserId;
        var mine = db.NonConformities.AsNoTracking().Where(n => n.ResponsibleUserId == me && (n.Status == NonConformityStatus.Open || n.Status == NonConformityStatus.InAction));
        var count = await mine.CountAsync(ct);
        if (count == 0)
        {
            return null;
        }

        var rows = await mine.OrderBy(n => n.OpenedAt).Take(AgendaLimits.EntriesPerCategory)
            .Select(n => new { n.Id, n.Number, n.Description, n.Status, n.OpenedAt }).ToListAsync(ct);
        var oldest = DateOnly.FromDateTime(BemAqui.BuildingBlocks.Time.BusinessTime.ToBusiness(rows[0].OpenedAt).DateTime);
        return new AgendaItem(
            NonConformitiesMine,
            AgendaText.Count(count, "não conformidade está com você", "não conformidades estão com você"),
            AgendaText.Sentence("a mais antiga foi aberta em " + AgendaText.DayMonth(oldest, request.Today)),
            QualityService.NonConformityLink.TrimEnd('/'), null, null, AgendaTier.Overdue, count, "Tratar NC",
            [.. rows.Select(n => new AgendaEntry(
                n.Number, (n.Status == NonConformityStatus.Open ? "Aberta · " : "Em ação · ") + (n.Description.Length > 70 ? n.Description[..69] + "…" : n.Description),
                QualityService.NonConformityLink + n.Id, null, null))]);
    }
}

/// <summary>Contrato <see cref="ILaborCostGaps"/>: o mesmo filtro do "Sem custo-hora" (<c>LaborCostService.Missing</c>), agrupado por cargo.</summary>
public sealed class LaborCostGapsContract(IProductionDb db) : ILaborCostGaps
{
    public async Task<IReadOnlyList<LaborCostGap>> ByPositionAsync(CancellationToken cancellationToken = default) =>
        await db.LaborEntries.AsNoTracking()
            .Where(e => e.CostMissing && e.EndedAt != null && e.CostVersion > 0 && e.CostPositionId != null)
            .GroupBy(e => e.CostPositionId!.Value)
            .Select(g => new LaborCostGap(g.Key, g.Count()))
            .ToListAsync(cancellationToken);
}
