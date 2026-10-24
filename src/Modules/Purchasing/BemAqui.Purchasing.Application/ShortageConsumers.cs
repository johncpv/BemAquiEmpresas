using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Inventory.Contracts;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Purchasing.Application;

/// <summary>
/// Consumidor de <see cref="StockShortageChanged"/> (CA 5, 6 e 8): o evento é só aviso de qual OS olhar (ADR 0023 §6); a quantidade vem
/// da fonte. Roda na transação da inbox (idempotente por mensagem): reentregas, eventos fora de ordem e faltas repetidas terminam no
/// mesmo estado. Com o módulo não liberado, a mensagem é dada como processada, sem escrita (o job diário recupera depois).
/// </summary>
public sealed class StockShortageChangedHandler(ShortageRecomposer recomposer) : IIntegrationEventConsumer<StockShortageChanged>
{
    public async Task HandleAsync(StockShortageChanged integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        await recomposer.RecomposeOrderAsync(integrationEvent.ProductionOrderId, cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Recomposição das faltas (CA 7, D3): na primeira execução com o módulo liberado, cria ou acerta as linhas de todas as OS com a marca
/// "Calculado na implantação" e grava a marca <c>shortages_backfilled</c>; nas seguintes (o job diário, a rede de segurança), repete a
/// mesma recomposição sem a marca. Módulo não liberado: nada, nem a marca (roda quando for liberado, CA 8).
/// </summary>
public sealed class ShortageBackfill(ShortageRecomposer recomposer, PurchasingParametersService parameters, IPurchasingDb db)
{
    /// <summary>Quantas linhas mudaram.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!recomposer.ModuleReleased)
        {
            return 0;
        }

        var onboarding = !await parameters.ShortagesBackfilledAsync(cancellationToken);
        var changed = await recomposer.RecomposeAllAsync(onboarding, cancellationToken);
        if (onboarding)
        {
            await parameters.MarkShortagesBackfilledAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }
}

public sealed record ShortageBackfillArgs;

/// <summary>Execução por tenant da recomposição da implantação (CA 7); argumentos só com o tenant.</summary>
public sealed class ShortageBackfillJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ShortageBackfillJob> logger)
    : TenantJob<ShortageBackfillArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, ShortageBackfillArgs args, CancellationToken cancellationToken) =>
        await services.GetRequiredService<ShortageBackfill>().RunAsync(cancellationToken);
}

/// <summary>Recorrente global que enfileira a recomposição por tenant ativo: disparado na subida (logo depois do deploy) e às 03:50 de São Paulo.</summary>
public sealed class ShortageBackfillRecurringJob(ITenantDirectory directory, IBackgroundJobClient jobs)
    : GlobalRecurringJob<ShortageBackfillJob, ShortageBackfillArgs>(directory, jobs)
{
    public const string Id = "purchasing-shortage-backfill";
    public const string Cron = "50 3 * * *";

    protected override string Queue => JobQueues.Maintenance;

    protected override ShortageBackfillArgs ArgsFor(TenantInfo tenant) => new();
}
