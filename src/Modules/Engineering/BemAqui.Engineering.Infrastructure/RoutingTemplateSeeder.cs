using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Engineering.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Engineering.Infrastructure;

/// <summary>Roteiro de exemplo (ENG-01 CA 8), depois dos centros e do calendário da Produção. Idempotente pela seed_key.</summary>
public sealed class RoutingTemplateSeeder : ITenantSeeder
{
    public int Order => 45;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        await tenantScope.GetRequiredService<RoutingTemplateService>().SeedExampleAsync(cancellationToken);
    }
}