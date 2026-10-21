using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Finance.Infrastructure;

/// <summary>Regra padrão "Proprietário aprova tudo" (FIN-03b CA 1); não volta se o tenant a excluiu. Depois dos grupos.</summary>
public sealed class ApprovalRuleSeeder : ITenantSeeder
{
    public int Order => 60;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        await tenantScope.GetRequiredService<ApprovalRuleService>().SeedDefaultAsync(cancellationToken);
    }
}
