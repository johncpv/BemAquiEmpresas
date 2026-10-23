using BemAqui.BuildingBlocks.Authorization;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Paging;
using BemAqui.BuildingBlocks.Persistence.Numbering;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Engineering.Contracts;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// OS de fabricação (PRD-01): nasce do item do projeto (automática para equipamento, pelo botão para componente), com as etapas
/// copiadas do roteiro sugerido e editáveis até a primeira etapa começar. Mesma tabela da OS de instalação, sempre filtrada pelo
/// tipo. Projeto, item, produto, centro e roteiro são só ids, validados pelos contratos dos donos (ADR 0006).
/// </summary>
public sealed partial class FabricationOrderService(
    IProductionDb db, IProjects projects, IParties parties, IItems items, IWorkCenters workCenters, IRoutingTemplates routings, INumberSequence numbers,
    IPermissionChecker permissions, ICurrentUser currentUser, IUserDirectory users, INotificationService notifications, IEventPublisher events, TimeProvider clock,
    ProductionBoardNotifier board, LaborWorkflow labor, IBomGate bom)
{
    public const string Link = "/app/producao/os/";

    public async Task<FabricationOrderPage> ListAsync(FabricationListQuery query, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(page);
        var orders = Fabrication(db.ProductionOrders.AsNoTracking());
        orders = query.Situation switch
        {
            "aberta" => orders.Where(o => o.Status == ProductionOrderStatus.Open),
            "andamento" => orders.Where(o => o.Status == ProductionOrderStatus.InProgress || o.Status == ProductionOrderStatus.Paused || o.Status == ProductionOrderStatus.Quality),
            // PRD-06a1 CA 1: "Aguardando você" de quem conclui a OS.
            "pronta" => orders.Where(o => o.Status == ProductionOrderStatus.ReadyToComplete),
            "concluida" => orders.Where(o => o.Status == ProductionOrderStatus.Completed),
            "cancelada" => orders.Where(o => o.Status == ProductionOrderStatus.Cancelled),
            _ => orders,
        };
        if (query.ProjectId is { } projectId)
        {
            orders = orders.Where(o => o.ProjectId == projectId);
        }

        if (!string.IsNullOrWhiteSpace(query.Line))
        {
            var line = query.Line.Trim();
            var centers = (await workCenters.ActiveAsync(cancellationToken)).Where(c => string.Equals(c.Line, line, StringComparison.OrdinalIgnoreCase)).Select(c => c.Id).ToList();
            orders = orders.Where(o => db.ProductionOrderSteps.Any(s => s.OrderId == o.Id && s.WorkCenterId != null && centers.Contains(s.WorkCenterId.Value)));
        }

        // Cliente e atraso vêm do projeto (Commercial): filtra pelos projetos que têm OS de fabricação.
        if (query.PartyId is not null || query.Late)
        {
            var projectIds = await orders.Select(o => o.ProjectId).Distinct().ToListAsync(cancellationToken);
            var chosen = (await projects.FindManyAsync(projectIds, cancellationToken))
                .Where(p => (query.PartyId is not { } party || p.PartyId == party) && (!query.Late || p.PromisedDate < Today)).Select(p => p.Id).ToList();
            orders = orders.Where(o => chosen.Contains(o.ProjectId));
            if (query.Late)
            {
                orders = orders.Where(o => o.Status != ProductionOrderStatus.Completed && o.Status != ProductionOrderStatus.Cancelled);
            }
        }

        var total = await orders.CountAsync(cancellationToken);
        var rows = await orders.OrderBy(o => o.Priority).ThenByDescending(o => o.CreatedAt).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken);
        var ids = rows.Select(o => o.Id).ToList();
        var steps = (await db.ProductionOrderSteps.AsNoTracking().Where(s => ids.Contains(s.OrderId)).OrderBy(s => s.Sequence)
            .Select(s => new { s.OrderId, s.Code, s.Name, s.Status }).ToListAsync(cancellationToken)).ToLookup(s => s.OrderId);
        var context = await ContextAsync(rows, cancellationToken);
        return new FabricationOrderPage(rows.Select(o =>
        {
            var project = context.Projects.GetValueOrDefault(o.ProjectId);
            var item = context.Items.GetValueOrDefault(o.ProjectItemId.GetValueOrDefault());
            var mine = steps[o.Id].ToList();
            return new FabricationOrderSummary(
                o.Id, o.Number, o.Status, o.ProjectId, project?.Number ?? string.Empty, project?.Name ?? string.Empty, project?.PartyId ?? Guid.Empty,
                project is null ? string.Empty : context.Parties.GetValueOrDefault(project.PartyId, string.Empty), o.ProjectItemId.GetValueOrDefault(), item?.Description ?? string.Empty,
                o.ProductId is { } product ? context.Products.GetValueOrDefault(product)?.Name : null, o.Quantity.GetValueOrDefault(), project?.PromisedDate,
                DelayDays(o.Status, project?.PromisedDate), mine.Select(s => s.Code ?? string.Empty).ToList(), mine.Select(s => s.Name).ToList(),
                FabricationRules.CurrentIndex(mine.Select(s => s.Status).ToList()), mine.Count == 0);
        }).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<Result<FabricationOrderDetail>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var order = await Fabrication(db.ProductionOrders.AsNoTracking()).FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (order is null)
        {
            return InstallationOrderService.NotFound();
        }

        var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == id).OrderBy(s => s.Sequence).ToListAsync(cancellationToken);
        var stepIds = steps.Select(s => s.Id).ToList();
        // PRD-05 CA 7: o checklist de cada etapa é o da tentativa dela (as tentativas anteriores da qualidade ficam na aba Qualidade).
        var attempts = steps.ToDictionary(s => s.Id, s => s.Attempt);
        var checklist = (await db.ProductionOrderChecklist.AsNoTracking().Where(c => c.OrderId == id && c.StepId != null).OrderBy(c => c.Sequence).ToListAsync(cancellationToken))
            .Where(c => attempts.GetValueOrDefault(c.StepId!.Value) == c.Attempt).ToLookup(c => c.StepId!.Value);
        var openNc = await db.NonConformities.AsNoTracking().CountAsync(n => n.OrderId == id && n.Status != NonConformityStatus.Closed, cancellationToken);
        var centers = (await workCenters.FindAsync(steps.Where(s => s.WorkCenterId is not null).Select(s => s.WorkCenterId!.Value).Distinct().ToList(), cancellationToken))
            .ToDictionary(c => c.Id);
        var context = await ContextAsync([order], cancellationToken);
        var project = context.Projects.GetValueOrDefault(order.ProjectId);
        var item = context.Items.GetValueOrDefault(order.ProjectItemId.GetValueOrDefault());
        var product = order.ProductId is { } productId ? context.Products.GetValueOrDefault(productId) : null;
        var routing = order.RoutingTemplateId is { } routingId && await routings.FindAsync([routingId], cancellationToken) is [var template, ..] ? template : null;
        var applied = await LaborReader.AppliedMinutesAsync(db, stepIds, clock.GetUtcNow(), cancellationToken);
        // PRD-06a1 CA 4 e 12: o motivo do botão "Concluir OS" desabilitado, quem concluiu e o último estorno.
        var blocker = order.Status is ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled ? null : await CompletionBlockerAsync(order, cancellationToken);
        var completedBy = order.Status == ProductionOrderStatus.Completed && order.CompletedBy is { } completer ? await users.DisplayNameAsync(completer, cancellationToken) : null;
        var reversedBy = order.CompletionReversedBy is { } reverser ? await users.DisplayNameAsync(reverser, cancellationToken) : null;
        var views = steps.Select(s => new FabricationStepView(
            s.Id, s.Sequence, s.Code ?? string.Empty, s.Name, s.WorkCenterId, s.WorkCenterId is { } c ? centers.GetValueOrDefault(c)?.Name : null,
            s.WorkCenterId is { } l ? centers.GetValueOrDefault(l)?.Line : null, s.StandardHours.GetValueOrDefault(), s.Status ?? ProductionStepStatus.Waiting, s.IsQualityStep,
            s.RequiresApproval, checklist[s.Id].Select(x => x.Text).ToList(), LaborRules.Hours(applied.GetValueOrDefault(s.Id)), s.AwaitingApprovalSince is not null,
            s.SelfApproved, s.SelfInspection)).ToList();
        return new FabricationOrderDetail(
            order.Id, order.Number, order.Status, order.ProjectId, project?.Number ?? string.Empty, project?.Name ?? string.Empty, project?.PartyId ?? Guid.Empty,
            project is null ? string.Empty : context.Parties.GetValueOrDefault(project.PartyId, string.Empty), project?.PromisedDate, order.ProjectItemId.GetValueOrDefault(),
            item?.Description ?? string.Empty, order.ProductId, product?.Code, product?.Name, order.Quantity.GetValueOrDefault(), order.Priority, order.RoutingTemplateId, routing?.Name,
            views, views.Sum(s => s.StandardHours), FabricationRules.CurrentIndex(steps.Select(s => s.Status).ToList()), FabricationRules.HasStarted(order.Status, steps.Select(s => s.Status)),
            DelayDays(order.Status, project?.PromisedDate), order.CancelReason, order.CancelledAt, order.CreatedAt, order.Version, order.QualityAttempt, order.SelfInspection,
            QualityRules.ReadyToComplete(steps.Select(s => s.Status)) && order.Status is not (ProductionOrderStatus.Completed or ProductionOrderStatus.Cancelled), openNc,
            order.Status == ProductionOrderStatus.Completed ? order.ActualEnd : null, completedBy, blocker, order.CompletionReversedAt, reversedBy, order.CompletionReversalReason);
    }

    /// <summary>Projetos, clientes, itens do projeto e produtos das OS, de uma vez por contrato.</summary>
    private async Task<(Dictionary<Guid, ProjectSummary> Projects, Dictionary<Guid, string> Parties, Dictionary<Guid, ProjectItemInfo> Items, Dictionary<Guid, ItemSummary> Products)> ContextAsync(
        IReadOnlyCollection<ProductionOrder> orders, CancellationToken ct)
    {
        var byProject = (await projects.FindManyAsync(orders.Select(o => o.ProjectId).Distinct().ToList(), ct)).ToDictionary(p => p.Id);
        var names = (await parties.FindManyAsync(byProject.Values.Select(p => p.PartyId).Distinct().ToList(), ct)).ToDictionary(p => p.Id, p => p.TradeName ?? p.LegalName);
        var projectItems = (await projects.FindItemsAsync(orders.Where(o => o.ProjectItemId is not null).Select(o => o.ProjectItemId!.Value).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        var products = (await items.FindAsync(orders.Where(o => o.ProductId is not null).Select(o => o.ProductId!.Value).Distinct().ToList(), ct)).ToDictionary(i => i.Id);
        return (byProject, names, projectItems, products);
    }

    /// <summary>Dias de atraso pela regra única do Kanban (PRD-02 CA 4).</summary>
    private int? DelayDays(ProductionOrderStatus status, DateOnly? promised) => ProductionOrderRisk.Evaluate(promised, Today, status, atLastStep: true).DelayDays;

    private static IQueryable<ProductionOrder> Fabrication(IQueryable<ProductionOrder> orders) => orders.Where(o => o.Type == ProductionOrderType.Fabrication);

    private DateOnly Today => BusinessTime.Today(clock);
}