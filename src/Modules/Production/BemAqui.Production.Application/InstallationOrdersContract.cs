using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Commercial.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Production.Application;

/// <summary>
/// <see cref="IInstallationOrders"/>: OS de instalação por projeto e situação, para o Commercial e o Finance. Só o tipo
/// Installation (PRD-01 CA 19): a OS de fabricação da mesma tabela devolve null e nunca é carregada aqui.
/// </summary>
public sealed class InstallationOrdersContract(IProductionDb db) : IInstallationOrders
{
    public async Task<IReadOnlyList<InstallationOrderInfo>> ByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var orders = await db.ProductionOrders.AsNoTracking().Where(o => o.ProjectId == projectId && o.Type == ProductionOrderType.Installation).OrderBy(o => o.Number).ToListAsync(cancellationToken);
        var ids = orders.Select(o => o.Id).ToList();
        var items = await db.ProductionOrderProjectItems.AsNoTracking().Where(i => ids.Contains(i.OrderId)).ToListAsync(cancellationToken);
        return orders.Select(o => new InstallationOrderInfo(o.Id, o.Number, State(o.Status), o.PlannedStart.GetValueOrDefault(), o.PlannedEnd.GetValueOrDefault(),
            items.Where(i => i.OrderId == o.Id).Select(i => i.ProjectItemId).ToList())).ToList();
    }

    public async Task<InstallationState?> StatusAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        await db.ProductionOrders.AsNoTracking().Where(o => o.Id == orderId && o.Type == ProductionOrderType.Installation).Select(o => (ProductionOrderStatus?)o.Status)
            .FirstOrDefaultAsync(cancellationToken) is { } status ? State(status) : null;

    public async Task<IReadOnlyDictionary<Guid, DateOnly>> LatestPlannedEndsAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projectIds);
        if (projectIds.Count == 0)
        {
            return new Dictionary<Guid, DateOnly>();
        }

        var ids = projectIds.Distinct().ToArray();
        // Mesma regra do ByProjectAsync lido um a um: fim sem data conta como a data mínima.
        var rows = await db.ProductionOrders.AsNoTracking()
            .Where(o => EF.Parameter(ids).Contains(o.ProjectId) && o.Type == ProductionOrderType.Installation && o.Status != ProductionOrderStatus.Cancelled)
            .GroupBy(o => o.ProjectId)
            .Select(g => new { g.Key, End = g.Max(o => o.PlannedEnd) })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Key, r => r.End ?? default);
    }

    /// <summary>Mapeamento explícito (PRD-01 CA 19): as situações da fabricação nunca lançam exceção aqui.</summary>
    internal static InstallationState State(ProductionOrderStatus status) => status switch
    {
        ProductionOrderStatus.Open => InstallationState.Open,
        ProductionOrderStatus.Completed => InstallationState.Completed,
        ProductionOrderStatus.Cancelled => InstallationState.Cancelled,
        _ => InstallationState.InProgress,
    };
}

/// <summary>
/// Contrato cancelado (COM-06 CA 14): cancela as OS de instalação abertas do projeto, com os títulos previstos; as iniciadas
/// ficam para decisão manual, com aviso ao responsável do projeto.
/// </summary>
public sealed class InstallationContractCancelledConsumer(IProductionDb db, InstallationOrderService orders, IProjects projects, INotificationService notifications)
    : IIntegrationEventConsumer<ContractCancelled>
{
    public async Task HandleAsync(ContractCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var open = await db.ProductionOrders.Where(o => o.ProjectId == integrationEvent.ProjectId && o.Type == ProductionOrderType.Installation
            && (o.Status == ProductionOrderStatus.Open || o.Status == ProductionOrderStatus.InProgress)).ToListAsync(cancellationToken);
        var started = new List<ProductionOrder>();
        foreach (var order in open)
        {
            if (order.Status == ProductionOrderStatus.Open)
            {
                await orders.CancelOrderAsync(order, "Contrato cancelado.", cancellationToken);
            }
            else
            {
                started.Add(order);
            }
        }

        if (started.Count > 0 && await projects.FindAsync(integrationEvent.ProjectId, cancellationToken) is { ManagerUserId: { } manager })
        {
            foreach (var order in started)
            {
                notifications.Notify(manager, "production.installation_contract_cancelled", $"Contrato cancelado com a OS {order.Number} iniciada: decida se continua ou cancela.", "/app/producao/instalacoes/" + order.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
