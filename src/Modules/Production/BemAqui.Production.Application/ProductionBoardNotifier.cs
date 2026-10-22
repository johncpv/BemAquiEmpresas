using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;

namespace BemAqui.Production.Application;

/// <summary>
/// Envio do aviso <c>OrderBoardChanged</c> aos grupos <c>{tenantId}:production</c> e <c>{tenantId}:shopfloor</c> (Host, SignalR).
/// A mensagem leva só o id da OS; o cliente recarrega pela API (PRD-02 CA 12 e 16).
/// </summary>
public interface IProductionBoardPush
{
    Task OrdersChangedAsync(Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default);
}

/// <summary>Sem hub no processo (CLI): nada a enviar.</summary>
public sealed class NoProductionBoardPush : IProductionBoardPush
{
    public Task OrdersChangedAsync(Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Aviso de mudança na OS ou na etapa para o Kanban (PRD-02 CA 12): chamado no fim do comando, sai só depois do commit pelo
/// <see cref="IAfterCommit"/> (também nos consumidores do outbox); rollback não envia nada. Nada passa pelo outbox.
/// </summary>
public sealed class ProductionBoardNotifier(IAfterCommit afterCommit, IProductionBoardPush push, ITenantContext tenant)
{
    public Task ChangedAsync(Guid orderId, CancellationToken cancellationToken = default) => ChangedAsync([orderId], cancellationToken);

    public Task ChangedAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        if (orderIds.Count == 0 || tenant.Tenant?.Id is not { } tenantId)
        {
            return Task.CompletedTask;
        }

        var ids = orderIds.Distinct().ToList();
        return afterCommit.RunAsync(ct => push.OrdersChangedAsync(tenantId, ids, ct), cancellationToken);
    }
}
