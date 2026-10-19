using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Local padrão do seed (CAD-03 CA 3): "ALM — Almoxarifado principal", pela <c>seed_key</c>. Idempotente, roda em todo
/// provisionamento e todo <c>migrate</c>, e não recria o que o tenant excluiu (busca também os excluídos).
/// </summary>
public sealed class LocationSeeder : ITenantSeeder
{
    public const string SeedKey = "alm";

    public int Order => 40;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IInventoryDb>();
        // Já semeado (mesmo excluído) ou o tenant já criou um ALM: nada a fazer.
        if (await db.Locations.IgnoreQueryFilters().AnyAsync(l => l.SeedKey == SeedKey, cancellationToken) || await db.Locations.AnyAsync(l => l.Code == "ALM", cancellationToken))
        {
            return;
        }

        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var hasDefault = await db.Locations.AnyAsync(l => l.IsDefault, cancellationToken);
        db.Locations.Add(new Location { SeedKey = SeedKey, Code = "ALM", Name = "Almoxarifado principal", IsDefault = !hasDefault, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }
}
