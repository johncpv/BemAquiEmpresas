using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Production.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Production.Infrastructure;

/// <summary>Modelo padrão da OS de instalação (COM-06 CA 4). Idempotente; não mexe no que o tenant mudou.</summary>
public sealed class InstallationTemplateSeeder : ITenantSeeder
{
    public int Order => 41;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        await tenantScope.GetRequiredService<InstallationTemplateService>().SeedAsync(cancellationToken);
    }
}
