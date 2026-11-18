using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Catalog.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Catalog.Infrastructure;

/// <summary>
/// Pacote do Comércio no Catálogo (PERF-04 CA 1): a tabela de preço "Balcão" e a categoria "Geral". Reusa os cadastros padrão da CAT-01
/// (<see cref="CatalogDefaults"/>), pela <c>seed_key</c>: se o primeiro uso do Catálogo já criou, nada é duplicado nem alterado.
/// </summary>
public sealed class CatalogRetailSeedPackage : IProfileSeedPackage
{
    public string Key => BusinessProfileSeeds.CatalogRetail;

    public Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        return tenantScope.GetRequiredService<CatalogDefaults>().EnsureAsync(cancellationToken);
    }
}
