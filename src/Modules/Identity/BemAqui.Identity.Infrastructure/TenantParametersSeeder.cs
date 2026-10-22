using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Infrastructure;

/// <summary>
/// Parâmetros iniciais (CAD-06 CA 15): 80,00% de encargos e precificação por markup, em todo provisionamento e todo
/// <c>migrate</c>. Só cria o que falta: valor já gravado pelo tenant nunca é sobrescrito.
/// </summary>
public sealed class TenantParametersSeeder : ITenantSeeder
{
    public int Order => 30;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IIdentityDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var existing = await db.TenantSettings.Select(s => s.Key).ToListAsync(cancellationToken);
        foreach (var (key, value) in new[]
        {
            (TenantParametersService.ChargesKey, "80.00"), (TenantParametersService.PricingKey, "markup"),
            // PRD-03b (ADR 0026 §1 e §3): teto de aparelhos da fábrica e prazo absoluto da sessão completa.
            (TenantSetting.MaxShopfloorDevices, "10"), (TenantSetting.SessionAbsoluteDays, "30"),
        })
        {
            if (!existing.Contains(key))
            {
                db.TenantSettings.Add(new TenantSetting { Key = key, Value = value, UpdatedAt = clock.GetUtcNow() });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
