using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Identity.Application.Shopfloor;

public sealed record ShopfloorDevicesArgs;

/// <summary>Execução por tenant (recebe o <c>tenantId</c>, ADR 0006): revoga os aparelhos sem uso há 30 dias e os vencidos (CA 8).</summary>
public sealed partial class ShopfloorDevicesJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ShopfloorDevicesJob> logger)
    : TenantJob<ShopfloorDevicesArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, ShopfloorDevicesArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(tenant);
        var revoked = await services.GetRequiredService<ShopfloorDeviceService>().RevokeStaleAsync(cancellationToken);
        var log = services.GetRequiredService<ILogger<ShopfloorDevicesJob>>();
        if (revoked > 0)
        {
            LogRevoked(log, revoked, tenant.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} aparelho(s) da fábrica revogado(s) por falta de uso ou validade no tenant {TenantId}.")]
    private static partial void LogRevoked(ILogger logger, int count, Guid tenantId);
}

/// <summary>Recorrente global diário (00:30 de São Paulo) que enfileira a revisão dos aparelhos por tenant ativo.</summary>
public sealed class ShopfloorDevicesRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<ShopfloorDevicesJob, ShopfloorDevicesArgs>(directory, jobs)
{
    public const string Id = "identity-shopfloor-devices";
    public const string Cron = "30 0 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override ShopfloorDevicesArgs ArgsFor(TenantInfo tenant) => new();
}