using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Domain;
using BemAqui.Engineering.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Commercial.Application;

/// <summary>
/// "Se eu aceitar este orçamento agora, quando entrego?" (PLN-05): o Commercial, dono do orçamento, lê itens e horas por centro da
/// composição (internamente, mesmo sem <c>quotes.view_cost</c>), monta o projeto virtual (<see cref="QuoteScheduleSimulation"/>) e chama o
/// <see cref="IProductionSchedule.SimulateDeliveryAsync"/>; o Production não lê tabela do Commercial. Nada é gravado (nem plano, nem fila, nem
/// orçamento) e a resposta tem só datas e dias (CA 9).
/// </summary>
public sealed class QuoteScheduleSimulationService(
    ICommercialDb db, IProductionSchedule schedule, IRoutingTemplates routings, IPermissionChecker permissions, TenantContextHolder tenant, TimeProvider clock)
{
    /// <summary>
    /// CA 11: o grupo da rota exige o módulo <c>quotes</c>; a simulação usa o motor do Production, então o módulo <c>production</c> precisa
    /// estar liberado no tenant (403 <c>module_not_released</c>), conferido aqui. Não exige <c>production.view</c> (CA 9).
    /// </summary>
    public Error? ProductionNotReleased() =>
        tenant.Resolved?.IsReleased("production") == true ? null : Error.Forbidden(Error.Codes.ModuleNotReleased, ModuleEndpoints.NotReleasedMessage);

    public async Task<Result<QuoteScheduleSimulationView>> SimulateAsync(Guid id, QuoteScheduleSimulationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (ProductionNotReleased() is { } notReleased)
        {
            return notReleased;
        }

        // IDOR (CA 11): o orçamento é do tenant (banco próprio) e a revisão precisa ser a corrente, em rascunho ou enviada.
        var quote = await db.Quotes.AsNoTracking().Where(q => q.Id == id).Select(q => new { q.Id, q.Number, q.Status, q.IsCurrent, q.DeliveryDays }).FirstOrDefaultAsync(cancellationToken);
        if (quote is null)
        {
            return QuoteService.NotFound();
        }

        if (!quote.IsCurrent)
        {
            return QuoteService.NotCurrent();
        }

        if (quote.Status is not (QuoteStatus.Draft or QuoteStatus.Sent))
        {
            return Error.Conflict("quote_not_simulable", "Só o orçamento em rascunho ou enviado simula prazo.");
        }

        var items = await db.QuoteItems.AsNoTracking().Where(i => i.QuoteId == id).OrderBy(i => i.Sequence)
            .Select(i => new { i.Id, i.Sequence, i.Kind, i.Quantity, i.ItemId, i.RoutingTemplateId }).ToListAsync(cancellationToken);
        var itemIds = items.Select(i => i.Id).ToList();
        var costs = db.QuoteItemCosts.AsNoTracking().Where(c => itemIds.Contains(c.QuoteItemId));
        var labor = await costs.Where(c => c.CostType == CostType.Labor && c.WorkCenterId != null).OrderBy(c => c.Id)
            .Select(c => new SimulationLaborLine(c.QuoteItemId, c.WorkCenterId!.Value, c.Quantity)).ToListAsync(cancellationToken);
        var withHours = labor.Select(l => l.QuoteItemId).ToHashSet();
        var candidates = items.Where(i => i.Kind != QuoteItemKind.Service && withHours.Contains(i.Id)).ToList();
        var simulationItems = new List<SimulationItem>(candidates.Count);
        if (candidates.Count > 0)
        {
            var active = (await routings.ActiveAsync(cancellationToken)).ToDictionary(t => t.Id);
            var byProduct = new Dictionary<Guid, Guid?>();
            foreach (var item in candidates)
            {
                if (item.ItemId is { } product && !byProduct.ContainsKey(product))
                {
                    byProduct[product] = (await routings.ForItemAsync(product, cancellationToken))?.Id;
                }

                var routing = PickRouting(item.RoutingTemplateId, item.ItemId is { } p ? byProduct[p] : null, active);
                var centers = routing is null ? [] : routing.Steps.OrderBy(s => s.Sequence).Where(s => s.WorkCenterId != null).Select(s => s.WorkCenterId!.Value).ToList();
                simulationItems.Add(new SimulationItem(item.Id, item.Sequence, item.Kind, item.Quantity, centers));
            }
        }

        var orders = QuoteScheduleSimulation.Build(simulationItems, labor);
        if (orders.Count == 0)
        {
            // CA 7: sem horas por centro, o motor nem é chamado.
            return new QuoteScheduleSimulationView(false, 0, 0, true, BusinessTime.Today(clock), null, null, null, null, false, quote.DeliveryDays, null, null, 0, null);
        }

        var hasInstallation = await costs.Where(c => c.CostType == CostType.Installation).SumAsync(c => (decimal?)c.Total, cancellationToken) > 0m;
        var virtualOrders = orders.Select(o => (IReadOnlyList<SimulatedStep>)[.. o.Steps.Select((s, k) => new SimulatedStep($"Etapa {k + 1}", s.WorkCenterId, s.Hours))]).ToList();
        var simulated = await schedule.SimulateDeliveryAsync(new SimulatedProject(Guid.CreateVersion7(), quote.Number, null, input.QueuePosition, virtualOrders), hasInstallation, cancellationToken);

        var promised = quote.DeliveryDays is { } days ? simulated.Today.AddDays(days) : (DateOnly?)null;
        // CA 4 e 9: número e nome de quem passa a atrasar só com projects.view; sem, só a contagem.
        IReadOnlyList<QuoteSimulationLateProject>? late = null;
        if (await permissions.HasPermissionAsync("projects", "view", cancellationToken))
        {
            var ids = simulated.NewlyLateProjectIds.ToList();
            var found = await db.Projects.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, p.Number, p.Name }).ToDictionaryAsync(p => p.Id, cancellationToken);
            late = [.. ids.Where(found.ContainsKey).Select(i => new QuoteSimulationLateProject(i, found[i].Number, found[i].Name))];
        }

        return new QuoteScheduleSimulationView(
            true, simulated.QueueSize, simulated.Position, simulated.Position > simulated.QueueSize, simulated.Today, simulated.FabricationEnd, simulated.DeliveryDate,
            simulated.DeliveryDate is { } delivery ? QuoteScheduleSimulation.CalendarDays(simulated.Today, delivery) : null, simulated.NoForecastReason, simulated.IncludesInstallation,
            quote.DeliveryDays, promised, QuoteScheduleSimulation.DaysBeyond(promised, simulated.DeliveryDate), simulated.NewlyLateProjectIds.Count, late);
    }

    /// <summary>
    /// Roteiro para a ordem das etapas (CA 2), como a sugestão da PRD-01 só entre os ativos: o do item do orçamento, o vinculado ao produto e,
    /// havendo um único roteiro ativo, ele. O "último usado com o produto" é dado das OS do Production e fica fora (sem roteiro, vale a
    /// ordem das linhas).
    /// </summary>
    private static RoutingTemplateInfo? PickRouting(Guid? itemRouting, Guid? productRouting, IReadOnlyDictionary<Guid, RoutingTemplateInfo> active)
    {
        foreach (var candidate in new[] { itemRouting, productRouting })
        {
            if (candidate is { } routingId && active.TryGetValue(routingId, out var found))
            {
                return found;
            }
        }

        return active.Count == 1 ? active.Values.First() : null;
    }
}
