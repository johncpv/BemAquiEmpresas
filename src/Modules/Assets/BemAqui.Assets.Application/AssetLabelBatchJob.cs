using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Assets.Application;

/// <summary>Argumentos do lote (CA 3, §4.3 da revisão): só ids e o leiaute, nunca nome nem dado do bem.</summary>
public sealed record AssetLabelBatchArgs(IReadOnlyList<Guid> AssetIds, AssetLabelLayout Layout, Guid RequestedBy);

/// <summary>"Imprimir etiquetas" acima de 50 (PAT-01b CA 3): gera o PDF no escopo do tenant e avisa quem pediu no sino.</summary>
public sealed class AssetLabelBatchJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<AssetLabelBatchJob> logger)
    : TenantJob<AssetLabelBatchArgs>(directory, scopes, logger)
{
    protected override Task RunAsync(IServiceProvider services, TenantInfo tenant, AssetLabelBatchArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        return services.GetRequiredService<AssetLabelService>().CompleteBatchAsync(args, cancellationToken);
    }
}
