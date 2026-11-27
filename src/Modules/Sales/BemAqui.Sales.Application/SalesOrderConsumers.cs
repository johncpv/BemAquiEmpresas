using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Contracts;
using BemAqui.Sales.Contracts;
using BemAqui.Sales.Domain;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Sales.Application;

/// <summary>
/// Reações do pedido às mudanças da cobrança (VND-02a), na transação do consumidor (outbox + inbox): a baixa do título vira o pedido pago uma vez só
/// (CA 18, 19, 21 e 24) e o boleto cancelado ou vencido sem pagamento devolve o pedido a aberto.
/// </summary>
public sealed class SalesOrderConsumers(SalesOrderService orders) : IIntegrationEventConsumer<ReceivableSettled>, IIntegrationEventConsumer<BillingSlipCancelled>
{
    public Task HandleAsync(ReceivableSettled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return integrationEvent.IsReversal ? Task.CompletedTask : orders.HandleSettledAsync(integrationEvent.ReceivableId, cancellationToken);
    }

    public Task HandleAsync(BillingSlipCancelled integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return orders.HandleSlipReleasedAsync(integrationEvent.ReceivableId, integrationEvent.Expired, cancellationToken);
    }
}

/// <summary>Leitura do pedido pelos outros módulos (<see cref="ISalesOrderLookup"/>), sem dado pessoal.</summary>
public sealed class SalesOrderLookup(ISalesDb db) : ISalesOrderLookup
{
    public Task<SalesOrderSummary?> FindAsync(Guid orderId, CancellationToken cancellationToken = default) =>
        db.SalesOrders.AsNoTracking().Where(o => o.Id == orderId).Select(Summary).FirstOrDefaultAsync(cancellationToken);

    public Task<SalesOrderSummary?> FindByExternalAsync(SalesOrderOrigin origin, Guid? sourceAppId, string externalId, CancellationToken cancellationToken = default) =>
        db.SalesOrders.AsNoTracking().Where(o => o.Origin == origin && o.SourceAppId == sourceAppId && o.ExternalId == externalId).Select(Summary).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<SalesOrderSummary>> ByCustomerAsync(Guid customerId, int limit = 20, CancellationToken cancellationToken = default) =>
        await db.SalesOrders.AsNoTracking().Where(o => o.CustomerId == customerId).OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id).Take(Math.Clamp(limit, 1, 100))
            .Select(Summary).ToListAsync(cancellationToken);

    private static readonly System.Linq.Expressions.Expression<Func<SalesOrder, SalesOrderSummary>> Summary = o =>
        new SalesOrderSummary(o.Id, o.Number, o.Status, o.Origin, o.SourceAppId, o.ExternalId, o.CustomerId, o.Total, o.SaleId, o.ReceivableId, o.CreatedAt, o.UpdatedAt);
}

public sealed record SalesOrderExpiryArgs;

/// <summary>Job <c>sales-order-expiry</c> de um tenant (CA 12): argumentos só com o <c>tenantId</c> (no <c>TenantJob</c>).</summary>
public sealed class SalesOrderExpiryJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<SalesOrderExpiryJob> logger)
    : TenantJob<SalesOrderExpiryArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, SalesOrderExpiryArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<SalesOrderService>().ExpireAsync(cancellationToken);
}

/// <summary>Recorrente global a cada 15 minutos que enfileira a expiração por tenant ativo.</summary>
public sealed class SalesOrderExpiryRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<SalesOrderExpiryJob, SalesOrderExpiryArgs>(directory, jobs)
{
    public const string Id = "sales-order-expiry";
    public const string Cron = "*/15 * * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override SalesOrderExpiryArgs ArgsFor(TenantInfo tenant) => new();
}
