using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.Engineering.Contracts;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Inventory.Domain;
using BemAqui.Production.Contracts;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Inventory.Application;

/// <summary>
/// Gatilhos da reconciliação das reservas (EST-02 CA 1, 9 e 10, ADR 0023 §6): o evento só diz qual OS olhar; o alvo vem da fonte.
/// Roda na transação da inbox (idempotente por mensagem). A origem gravada é a da auditoria do consumidor ("Evento {tipo} · mensagem
/// {id}"). Na liberação e na revisão da BOM, quem libera BOM (<c>engineering.approve</c>) recebe o aviso curto do fim da reserva
/// (padrão 7, decisão do PO de 23/10/2026): "Material reservado: {n} itens, {f} em falta.", na mesma transação (sai depois do commit).
/// </summary>
public sealed class ReservationConsumers(
    StockReservationService reservations, AuditOrigin origin, IInventoryDb db, IUserDirectory users, INotificationService notifications, TenantContextHolder tenant)
    : IIntegrationEventConsumer<ProductionOrderCancelled>, IIntegrationEventConsumer<ProductionOrderCompleted>, IIntegrationEventConsumer<BomReleased>,
        IIntegrationEventConsumer<BomRevised>
{
    public const string NoticeType = "inventory.reservation_done";

    /// <summary>"Material reservado: 3 itens, 1 em falta." (padrão 7).</summary>
    public static string Notice(int items, int missing) => $"Material reservado: {items} {(items == 1 ? "item" : "itens")}, {missing} em falta.";

    public Task HandleAsync(BomReleased integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return ReconcileAndNotifyAsync(integrationEvent.ProductionOrderId, Origin(nameof(BomReleased)), cancellationToken);
    }

    /// <summary>Revisão antiga é ignorada sem olhar o número: a reconciliação lê a revisão vigente da fonte (CA 2).</summary>
    public Task HandleAsync(BomRevised integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return ReconcileAndNotifyAsync(integrationEvent.ProductionOrderId, Origin(nameof(BomRevised)), cancellationToken);
    }

    private async Task ReconcileAndNotifyAsync(Guid orderId, string label, CancellationToken ct)
    {
        await reservations.ReconcileOrderAsync(orderId, label, cancellationToken: ct);
        if (tenant.Resolved?.IsReleased(StockReservationService.Module) != true)
        {
            return;
        }

        var rows = await db.Reservations.AsNoTracking().Where(r => r.ProductionOrderId == orderId && r.Status == Contracts.ReservationState.Active)
            .Select(r => new { r.ItemId, Short = r.RequestedQuantity > r.Quantity + r.QuantityIssued }).ToListAsync(ct);
        if (rows.Count == 0)
        {
            return;
        }

        var text = Notice(rows.Select(r => r.ItemId).Distinct().Count(), rows.Where(r => r.Short).Select(r => r.ItemId).Distinct().Count());
        foreach (var user in await users.UsersWithPermissionAsync("engineering.approve", ct))
        {
            notifications.Notify(user, NoticeType, text, "/app/producao/os/" + orderId + "?aba=material");
        }

        await db.SaveChangesAsync(ct);
    }

    public Task HandleAsync(ProductionOrderCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return reservations.ReconcileOrderAsync(integrationEvent.ProductionOrderId, Origin(nameof(ProductionOrderCancelled)), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// CA 10: só reconcilia (alvo 0). Na PRD-06a2 a conclusão já liberou de forma síncrona pelo <c>ReleaseForOrderAsync</c> e aqui nada
    /// muda; nas OS concluídas antes dela (PRD-06a1), libera o que houver.
    /// </summary>
    public Task HandleAsync(ProductionOrderCompleted integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return reservations.ReconcileOrderAsync(integrationEvent.ProductionOrderId, Origin(nameof(ProductionOrderCompleted)), cancellationToken: cancellationToken);
    }

    private string Origin(string type) => string.IsNullOrWhiteSpace(origin.Label) ? "Evento " + type : origin.Label;
}

/// <summary>
/// Recomposição da implantação (D3, ADR 0025 §3, CA 12): reconcilia cada OS em aberto com BOM liberada e cada OS que ainda tem reserva
/// ativa (as concluídas ou canceladas ficam com alvo 0), com a origem "Recomposição" e a marca "Calculado na implantação", uma OS por
/// transação. Idempotente: a reconciliação leva ao alvo, então rodar de novo, ou junto com o consumidor ao vivo (a trava da OS e a do
/// item os põem em fila), não grava nada a mais; ao terminar, grava <c>inventory.parameters.reservations_backfilled_at</c> e as
/// próximas execuções não fazem nada. Com o módulo <c>inventory</c> não liberado, não faz nada nem marca (roda quando for liberado).
/// </summary>
public sealed class ReservationBackfill(IInventoryDb db, StockReservationService reservations, IReleasedBomSource boms, TenantContextHolder tenant, TimeProvider clock)
{
    /// <summary>Quantas OS foram reconciliadas.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (tenant.Resolved?.IsReleased(StockReservationService.Module) != true)
        {
            return 0;
        }

        var parameters = await db.InventoryParameters.FirstOrDefaultAsync(cancellationToken);
        if (parameters?.ReservationsBackfilledAt is not null)
        {
            return 0;
        }

        var open = await boms.OpenOrdersWithReleasedBomAsync(cancellationToken);
        var withActive = await db.Reservations.AsNoTracking().Where(r => r.Status == Contracts.ReservationState.Active).Select(r => r.ProductionOrderId).Distinct().ToListAsync(cancellationToken);
        var done = 0;
        foreach (var orderId in open.Union(withActive).Distinct().Order())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await reservations.ReconcileOrderAsync(orderId, ReservationOrigins.Backfill, isBackfill: true, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            done++;
        }

        if (parameters is not null)
        {
            parameters.ReservationsBackfilledAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        return done;
    }
}

public sealed record ReservationBackfillArgs;

/// <summary>Execução por tenant da recomposição (CA 12); argumentos só de ids (o tenant).</summary>
public sealed class ReservationBackfillJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ReservationBackfillJob> logger)
    : TenantJob<ReservationBackfillArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, ReservationBackfillArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<ReservationBackfill>().RunAsync(cancellationToken);
}

/// <summary>
/// Recorrente global que enfileira a recomposição por tenant ativo: disparado na subida do processo (logo depois do deploy) e
/// repetido de madrugada (03:45 de São Paulo), o que também cobre a BOM liberada antes de o módulo <c>inventory</c> ser liberado.
/// </summary>
public sealed class ReservationBackfillRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<ReservationBackfillJob, ReservationBackfillArgs>(directory, jobs)
{
    public const string Id = "inventory-reservation-backfill";
    public const string Cron = "45 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override ReservationBackfillArgs ArgsFor(TenantInfo tenant) => new();
}
