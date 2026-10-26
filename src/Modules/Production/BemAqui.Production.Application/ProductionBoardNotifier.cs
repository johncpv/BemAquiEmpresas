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

    /// <summary>Sinal <c>ScheduleChanged</c> (PLN-01b CA 4): o plano novo foi gravado; só o sinal, sem dado (a TV e o Planejamento recarregam).</summary>
    Task ScheduleChangedAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Sem hub no processo (CLI): nada a enviar.</summary>
public sealed class NoProductionBoardPush : IProductionBoardPush
{
    public Task OrdersChangedAsync(Guid tenantId, IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Aviso de mudança na OS ou na etapa para o Kanban (PRD-02 CA 12): chamado no fim do comando, sai só depois do commit pelo
/// <see cref="IAfterCommit"/> (também nos consumidores do outbox); rollback não envia nada. Nada passa pelo outbox. PLN-01b CA 1: toda
/// mudança na OS ou na etapa que passa por aqui (gerar, editar etapas, trocar roteiro, cancelar, iniciar, pausar, retomar, finalizar e
/// concluir etapa, qualidade, conclusão e estorno da conclusão) também pede o recálculo do plano, depois do commit.
/// </summary>
public sealed class ProductionBoardNotifier(IAfterCommit afterCommit, IProductionBoardPush push, ITenantContext tenant, IScheduleTrigger trigger)
{
    public Task ChangedAsync(Guid orderId, CancellationToken cancellationToken = default) => ChangedAsync([orderId], cancellationToken);

    public async Task ChangedAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderIds);
        if (orderIds.Count == 0 || tenant.Tenant?.Id is not { } tenantId)
        {
            return;
        }

        var ids = orderIds.Distinct().ToList();
        await afterCommit.RunAsync(ct => push.OrdersChangedAsync(tenantId, ids, ct), cancellationToken);
        await trigger.RequestAsync("OS", cancellationToken);
    }
}
