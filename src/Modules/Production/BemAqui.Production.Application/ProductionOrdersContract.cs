using BemAqui.BuildingBlocks.Messaging;
using BemAqui.Commercial.Contracts;
using BemAqui.Engineering.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary><see cref="IProductionOrders"/> (PRD-01 CA 21): só o tipo Fabrication, com a etapa atual e sem custo.</summary>
public sealed class ProductionOrdersContract(IProductionDb db) : IProductionOrders
{
    public async Task<ProductionOrderInfo?> FindAsync(Guid id, CancellationToken cancellationToken = default) =>
        await InfosAsync(db.ProductionOrders.Where(o => o.Id == id), cancellationToken) is [var info, ..] ? info : null;

    public async Task<IReadOnlyList<ProductionOrderInfo>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        return ids.Count == 0 ? [] : await InfosAsync(db.ProductionOrders.Where(o => ids.Contains(o.Id)), cancellationToken);
    }

    public async Task<IReadOnlyList<ProductionOrderInfo>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        await InfosAsync(db.ProductionOrders.Where(o => o.ProjectId == projectId), cancellationToken);

    public async Task<ProductionOrderState?> StatusAsync(Guid id, CancellationToken cancellationToken = default) =>
        await db.ProductionOrders.AsNoTracking().Where(o => o.Id == id && o.Type == ProductionOrderType.Fabrication).Select(o => (ProductionOrderStatus?)o.Status)
            .FirstOrDefaultAsync(cancellationToken) is { } status ? State(status) : null;

    private async Task<IReadOnlyList<ProductionOrderInfo>> InfosAsync(IQueryable<ProductionOrder> query, CancellationToken ct)
    {
        var orders = await query.AsNoTracking().Where(o => o.Type == ProductionOrderType.Fabrication).OrderBy(o => o.Number).ToListAsync(ct);
        var ids = orders.Select(o => o.Id).ToList();
        var steps = (await db.ProductionOrderSteps.AsNoTracking().Where(s => ids.Contains(s.OrderId)).OrderBy(s => s.Sequence)
            .Select(s => new { s.OrderId, s.Name, s.Code, s.Status }).ToListAsync(ct)).ToLookup(s => s.OrderId);
        return orders.Select(o =>
        {
            var mine = steps[o.Id].ToList();
            var index = FabricationRules.CurrentIndex(mine.Select(s => s.Status).ToList());
            return new ProductionOrderInfo(o.Id, o.Number, ProductionOrderKind.Fabrication, o.ProjectId, o.ProjectItemId.GetValueOrDefault(), o.ProductId, o.Quantity.GetValueOrDefault(),
                State(o.Status), index < 0 ? null : mine[index].Name, index < 0 ? null : mine[index].Code,
                o.Status == ProductionOrderStatus.Completed ? o.ActualEnd : null, o.Status == ProductionOrderStatus.Completed ? o.CompletedBy : null,
                o.CompletionReversedAt, o.CompletionReversedBy, o.CompletionReversalReason);
        }).ToList();
    }

    internal static ProductionOrderState State(ProductionOrderStatus status) => status switch
    {
        ProductionOrderStatus.Open => ProductionOrderState.Open,
        ProductionOrderStatus.InProgress => ProductionOrderState.InProgress,
        ProductionOrderStatus.Paused => ProductionOrderState.Paused,
        ProductionOrderStatus.Quality => ProductionOrderState.Quality,
        ProductionOrderStatus.Completed => ProductionOrderState.Completed,
        ProductionOrderStatus.ReadyToComplete => ProductionOrderState.ReadyToComplete,
        _ => ProductionOrderState.Cancelled,
    };
}

/// <summary>Item do projeto criado (PRD-01 CA 1 a 4): o equipamento vira OS de fabricação com o roteiro sugerido.</summary>
public sealed class FabricationProjectItemCreatedConsumer(FabricationOrderService orders) : IIntegrationEventConsumer<ProjectItemCreated>
{
    public Task HandleAsync(ProjectItemCreated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return orders.GenerateFromEventAsync(integrationEvent, cancellationToken);
    }
}

/// <summary>Contrato cancelado (PRD-01 CA 18): handler próprio da fabricação, separado do da instalação (COM-06).</summary>
public sealed class FabricationContractCancelledConsumer(FabricationOrderService orders) : IIntegrationEventConsumer<ContractCancelled>
{
    public Task HandleAsync(ContractCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return orders.CancelForContractAsync(integrationEvent.ProjectId, cancellationToken);
    }
}

/// <summary>
/// Uso de roteiro e de centro pelas OS de fabricação não canceladas (ENG-01 CA 10, CAD-03 CA 14): em uso, o roteiro e o centro só
/// inativam.
/// </summary>
public sealed class FabricationUsage(IProductionDb db) : IRoutingTemplateUsageProvider, IWorkCenterUsageProvider
{
    public string Noun => "OS";

    public async Task<IReadOnlyDictionary<Guid, int>> CountAsync(IReadOnlyCollection<Guid> templateIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(templateIds);
        return await db.ProductionOrders.AsNoTracking()
            .Where(o => o.Type == ProductionOrderType.Fabrication && o.Status != ProductionOrderStatus.Cancelled && o.RoutingTemplateId != null && templateIds.Contains(o.RoutingTemplateId.Value))
            .GroupBy(o => o.RoutingTemplateId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    public async Task<string?> UsageAsync(Guid workCenterId, CancellationToken cancellationToken = default) =>
        await db.ProductionOrderSteps.AnyAsync(s => s.WorkCenterId == workCenterId
            && db.ProductionOrders.Any(o => o.Id == s.OrderId && o.Type == ProductionOrderType.Fabrication && o.Status != ProductionOrderStatus.Cancelled), cancellationToken)
            ? "OS de fabricação"
            : null;
}