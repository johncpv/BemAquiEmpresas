using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

public sealed partial class QualityService
{
    /// <summary>
    /// Aba Qualidade da OS (CA 7 e 15): cada tentativa com o resultado, quem decidiu, a marca "Autoinspeção", a NC e os itens do checklist
    /// daquela tentativa (a mais nova primeiro), a tentativa em andamento e as NC da OS. Com <c>production.view</c>.
    /// </summary>
    public async Task<Result<OrderQualityView>> OrderAsync(Guid orderId, NonConformityService nonConformities, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nonConformities);
        var order = await db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId && o.Type == ProductionOrderType.Fabrication, cancellationToken);
        if (order is null)
        {
            return InstallationOrderService.NotFound();
        }

        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == orderId).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var byId = steps.ToDictionary(s => s.Id);
        var quality = steps.Where(s => s.IsQualityStep).Select(s => s.Id).ToList();
        var checklist = (await db.ProductionOrderChecklist.AsNoTracking().Where(c => c.StepId != null && quality.Contains(c.StepId.Value)).OrderBy(c => c.Sequence).ToListAsync(cancellationToken))
            .ToLookup(c => (c.StepId!.Value, c.Attempt));
        var decisions = await db.QualityDecisions.AsNoTracking().Where(d => d.OrderId == orderId).OrderByDescending(d => d.DecidedAt).ToListAsync(cancellationToken);
        var ncs = await db.NonConformities.AsNoTracking().Where(n => n.OrderId == orderId).ToDictionaryAsync(n => n.Id, cancellationToken);
        var names = await NamesAsync(decisions.Select(d => d.DecidedBy).Concat(checklist.SelectMany(g => g.Select(c => c.MarkedBy))), cancellationToken);
        List<QualityAttemptItem> Items(Guid stepId, int attempt) =>
            checklist[(stepId, attempt)].Select(c => new QualityAttemptItem(c.Text, c.Result, Name(names, c.MarkedBy), c.MarkedAt)).ToList();

        var attempts = new List<QualityAttemptView>();
        // A tentativa em andamento: a etapa de qualidade decidível (ou que espera o retrabalho) ainda sem decisão nesta tentativa.
        foreach (var pending in steps.Where(s => s.IsQualityStep && s.Status != ProductionStepStatus.Done && !decisions.Any(d => d.StepId == s.Id && d.Attempt == s.Attempt)))
        {
            if (pending.Status is ProductionStepStatus.Ready or ProductionStepStatus.InProgress or ProductionStepStatus.Paused)
            {
                attempts.Add(new QualityAttemptView(pending.Attempt, pending.Id, pending.Code ?? string.Empty, pending.Name, null, null, null, false, null, null, null, Items(pending.Id, pending.Attempt)));
            }
        }

        foreach (var d in decisions)
        {
            var step = byId.GetValueOrDefault(d.StepId);
            var nc = d.NonConformityId is { } id ? ncs.GetValueOrDefault(id) : null;
            // Os itens da decisão: na reprovação os da tentativa decidida; na aprovação, os da tentativa do checklist naquela hora.
            var itemsAttempt = checklist.Contains((d.StepId, d.Attempt)) ? d.Attempt : step?.Attempt ?? d.Attempt;
            attempts.Add(new QualityAttemptView(
                d.Attempt, d.StepId, step?.Code ?? string.Empty, step?.Name ?? string.Empty, d.Result, d.DecidedAt, Name(names, d.DecidedBy), d.SelfInspection, nc?.Id, nc?.Number,
                nc is not null ? byId.GetValueOrDefault(nc.ReturnToStepId)?.Name : null, Items(d.StepId, itemsAttempt)));
        }

        var list = await nonConformities.ListAsync(new NonConformityListQuery("todas", null, orderId, null), new BemAqui.BuildingBlocks.Paging.PageRequest(1, 100), cancellationToken);
        return new OrderQualityView(order.Id, order.Number, order.QualityAttempt, order.SelfInspection, QualityRules.ReadyToComplete(steps.Select(s => s.Status)), attempts, list.Items);
    }

    /// <summary>Detalhe da etapa para o /fab e o backoffice (CA 3 a 6): checklist, retorno sugerido, responsáveis e bloqueio.</summary>
    private async Task<Result<QualityStepDetail>> DetailAsync(Guid stepId, CancellationToken ct)
    {
        var cards = await BuildAsync([stepId], ct);
        if (!cards.TryGetValue(stepId, out var card))
        {
            return StepNotFound();
        }

        var order = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == card.OrderId, ct);
        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == order.Id).OrderBy(s => s.Sequence).ToListAsync(ct);
        var step = steps.Single(s => s.Id == stepId);
        var index = steps.IndexOf(step);
        var states = steps.Select(s => new QualityStepState(s.Status, s.IsQualityStep)).ToList();
        var suggested = QualityRules.SuggestedReturn(states, index);
        var returns = QualityRules.ReturnOptions(states, index).Select(i =>
        {
            var waiting = QualityRules.PlanRejection(states, index, i).BackToWaiting.Count;
            return new QualityReturnOption(steps[i].Id, steps[i].Code ?? string.Empty, steps[i].Name, i == suggested, waiting, QualityRules.RejectionImpact(steps[i].Name, waiting));
        }).ToList();
        var checklist = await db.ProductionOrderChecklist.AsNoTracking().Where(c => c.StepId == stepId && c.Attempt == step.Attempt).OrderBy(c => c.Sequence).ToListAsync(ct);
        var names = await NamesAsync(checklist.Select(c => c.MarkedBy), ct);
        var responsibles = new List<QualityUserOption>();
        foreach (var id in await users.UsersWithPermissionAsync("production.view", ct))
        {
            responsibles.Add(new QualityUserOption(id, await users.DisplayNameAsync(id, ct) ?? "Usuário"));
        }

        var me = currentUser.UserId;
        var myEmployee = me is { } user ? await links.EmployeeOfUserAsync(user, ct) : null;
        var started = myEmployee is { } employee && await db.LaborEntries.AsNoTracking().AnyAsync(e => e.OrderStepId == stepId && e.EmployeeId == employee && e.EndedAt == null, ct);
        var next = index + 1 < steps.Count ? steps[index + 1].Name : null;
        var product = order.ProductId is { } productId && await items.FindAsync([productId], ct) is [var found, ..] ? found : null;
        return new QualityStepDetail(
            card, product?.Code, order.Quantity.GetValueOrDefault(),
            checklist.Select(c => new QualityChecklistItem(c.Id, c.Text, c.Result, c.MarkedAt, Name(names, c.MarkedBy))).ToList(), returns,
            responsibles.OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList(), me is { } m && responsibles.Any(r => r.Id == m) ? m : null, next, QualityRules.ApprovalImpact(next),
            card.CanDecide ? null : QualityRules.SegregationMessage, started, !started && myEmployee is not null, order.Version, clock.GetUtcNow());
    }

    /// <summary>Cartões da fila (CA 2), sem N+1: OS, letras, produto, projeto e cliente de uma vez; segregação por OS.</summary>
    private async Task<Dictionary<Guid, QualityQueueItem>> BuildAsync(IReadOnlyCollection<Guid> stepIds, CancellationToken ct)
    {
        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => stepIds.Contains(s.Id)).ToListAsync(ct);
        var orderIds = steps.Select(s => s.OrderId).Distinct().ToList();
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, ct);
        var all = (await db.ProductionOrderSteps.AsNoTracking().Where(s => orderIds.Contains(s.OrderId)).OrderBy(s => s.Sequence).ToListAsync(ct)).ToLookup(s => s.OrderId);
        var openNc = (await db.NonConformities.AsNoTracking().Where(n => orderIds.Contains(n.OrderId) && n.Status != NonConformityStatus.Closed).GroupBy(n => n.OrderId)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct)).ToDictionary(g => g.Key, g => g.Count);
        var byProject = (await projects.FindManyAsync(orders.Values.Select(o => o.ProjectId).Distinct().ToList(), ct)).ToDictionary(p => p.Id);
        var partyNames = (await parties.FindManyAsync(byProject.Values.Select(p => p.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var products = (await items.FindAsync(orders.Values.Where(o => o.ProductId is not null).Select(o => o.ProductId!.Value).Distinct().ToList(), ct)).ToDictionary(i => i.Id, i => i.Name);
        var projectItems = (await projects.FindItemsAsync(orders.Values.Where(o => o.ProjectItemId is not null).Select(o => o.ProjectItemId!.Value).Distinct().ToList(), ct))
            .ToDictionary(i => i.Id, i => i.Description);
        var approvers = await users.UsersWithPermissionAsync(Permission, ct);
        var me = currentUser.UserId ?? Guid.Empty;
        var result = new Dictionary<Guid, QualityQueueItem>();
        foreach (var s in steps)
        {
            var order = orders[s.OrderId];
            var siblings = all[s.OrderId].ToList();
            var index = siblings.FindIndex(x => x.Id == s.Id);
            var project = byProject.GetValueOrDefault(order.ProjectId);
            var segregation = QualityRules.Segregation(me, await InvolvedAsync(order, ct), approvers);
            var product = order.ProductId is { } p ? products.GetValueOrDefault(p) : null;
            result[s.Id] = new QualityQueueItem(
                s.Id, order.Id, order.Number, s.Code ?? string.Empty, s.Name, index, siblings.Select(x => x.Code ?? string.Empty).ToList(), siblings.Select(x => x.Name).ToList(),
                s.Status ?? ProductionStepStatus.Waiting, product ?? (order.ProjectItemId is { } pi ? projectItems.GetValueOrDefault(pi, string.Empty) : string.Empty),
                project?.Number ?? string.Empty, project?.Name ?? string.Empty, project is null ? string.Empty : partyNames.GetValueOrDefault(project.PartyId, string.Empty),
                project?.PromisedDate, order.QualityAttempt, index > 0 ? siblings[index - 1].CompletedAt : null, segregation != QualitySegregation.Forbidden,
                segregation == QualitySegregation.SelfInspection, openNc.GetValueOrDefault(order.Id));
        }

        return result;
    }

    private async Task<Dictionary<Guid, string?>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var names = new Dictionary<Guid, string?>();
        foreach (var id in ids.Where(i => i is not null).Select(i => i!.Value).Distinct())
        {
            names[id] = await users.DisplayNameAsync(id, ct);
        }

        return names;
    }

    private static string? Name(Dictionary<Guid, string?> names, Guid? id) => id is { } value ? names.GetValueOrDefault(value) : null;
}