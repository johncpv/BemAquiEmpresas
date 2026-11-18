using BemAqui.BuildingBlocks.Jobs;
using BemAqui.BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BemAqui.Catalog.Application;

/// <summary>
/// Importação grande (CAT-02, acima de <see cref="ProductImportService.SyncMaxRows"/> linhas): roda no escopo do tenant (só o <c>tenantId</c> e
/// os ids nos argumentos), grava o progresso a cada bloco e avisa no sino quem confirmou, com o link para o relatório.
/// </summary>
public sealed class ProductImportJob(ITenantDirectory directory, ITenantScopeFactory scopes, ILogger<ProductImportJob> logger)
    : TenantJob<ProductImportJobArgs>(directory, scopes, logger)
{
    protected override async Task RunAsync(IServiceProvider services, TenantInfo tenant, ProductImportJobArgs args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);
        var service = services.GetRequiredService<ProductImportService>();
        await service.ExecuteAsync(args.RunId, cancellationToken);
        await service.NotifyDoneAsync(args, cancellationToken);
    }
}
