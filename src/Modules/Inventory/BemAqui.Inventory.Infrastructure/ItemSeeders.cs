using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Unidades do seed (CAD-02a CA 1, R-PLT-3): UN, KG, M, M2, M3, L e PC, pela <c>seed_key</c>. Idempotente e não recria a que o
/// tenant excluiu; código que o tenant já criou à mão não é duplicado.
/// </summary>
public sealed class UnitSeeder : ITenantSeeder
{
    public static readonly IReadOnlyList<(string Code, string Name)> Seeds =
        [("UN", "Unidade"), ("KG", "Quilograma"), ("M", "Metro"), ("M2", "Metro quadrado"), ("M3", "Metro cúbico"), ("L", "Litro"), ("PC", "Peça")];

    public int Order => 40;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IInventoryDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var seeded = (await db.Units.IgnoreQueryFilters().Where(u => u.SeedKey != null).Select(u => u.SeedKey!).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var codes = (await db.Units.Select(u => u.Code).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        foreach (var (code, name) in Seeds.Where(s => !seeded.Contains(s.Code.ToLowerInvariant()) && !codes.Contains(s.Code)))
        {
            db.Units.Add(new MeasureUnit { SeedKey = code.ToLowerInvariant(), Code = code, Name = name, CreatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Garantia padrão do seed (CA 4, P-11): Produto acabado 12 meses, Componente e Serviço 3, Matéria-prima e Material de consumo
/// sem garantia. Só cria o tipo que falta; nunca sobrescreve o valor editado.
/// </summary>
public sealed class WarrantyDefaultSeeder : ITenantSeeder
{
    public static readonly IReadOnlyDictionary<ItemType, int?> Seeds = new Dictionary<ItemType, int?>
    {
        [ItemType.RawMaterial] = null, [ItemType.Component] = 3, [ItemType.Finished] = 12, [ItemType.Consumable] = null, [ItemType.Service] = 3,
    };

    public int Order => 40;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IInventoryDb>();
        var existing = (await db.WarrantyDefaults.Select(w => w.ItemType).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var (type, months) in Seeds.Where(s => !existing.Contains(s.Key)))
        {
            db.WarrantyDefaults.Add(new WarrantyDefault { ItemType = type, Months = months });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
