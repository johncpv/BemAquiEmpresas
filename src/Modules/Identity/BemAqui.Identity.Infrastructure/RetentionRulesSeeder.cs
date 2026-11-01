using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Application.Privacy;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Infrastructure;

/// <summary>
/// Tabela de retenção pré-configurada (LGPD-01b CA 1, ADR 0018), em todo provisionamento e todo <c>migrate</c>: só cria a linha que falta pelo
/// <c>seed_key</c>; a regra editada pelo tenant nunca é sobrescrita.
/// </summary>
public sealed class RetentionRulesSeeder : ITenantSeeder
{
    public int Order => 31;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IIdentityDb>();
        if (await RetentionRuleService.SeedMissingAsync(db, tenantScope.GetRequiredService<TimeProvider>(), cancellationToken) > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
