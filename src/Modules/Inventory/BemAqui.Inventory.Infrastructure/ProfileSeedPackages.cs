using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Pacote do Comércio no Estoque (PERF-04 CA 1): locais "Loja" e "Depósito", pela <c>seed_key</c>. Não recria o que o tenant excluiu, não
/// duplica um código que o tenant já usa e não muda o local padrão que já existe (o "Loja" só vira padrão se não houver nenhum).
/// </summary>
public sealed class InventoryRetailSeedPackage : IProfileSeedPackage
{
    public static readonly IReadOnlyList<(string Key, string Code, string Name)> Seeds = [("loja", "LOJA", "Loja"), ("deposito", "DEP", "Depósito")];

    public string Key => BusinessProfileSeeds.InventoryRetail;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IInventoryDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var seeded = (await db.Locations.IgnoreQueryFilters().Where(l => l.SeedKey != null).Select(l => l.SeedKey!).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var codes = (await db.Locations.Select(l => l.Code).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasDefault = await db.Locations.AnyAsync(l => l.IsDefault, cancellationToken);
        foreach (var (key, code, name) in Seeds.Where(s => !seeded.Contains(s.Key) && !codes.Contains(s.Code)))
        {
            db.Locations.Add(new Location { SeedKey = key, Code = code, Name = name, IsDefault = !hasDefault, CreatedAt = clock.GetUtcNow() });
            hasDefault = true;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Pacote do segmento de alimentação no Estoque (PERF-04 CA 4): unidades de padaria, restaurante, lanchonete e trailer, pela
/// <c>seed_key</c>, com as mesmas regras do <see cref="UnitSeeder"/>. As categorias do segmento são do Catálogo (CAT-01).
/// </summary>
public sealed class InventoryFoodSeedPackage : IProfileSeedPackage
{
    public static readonly IReadOnlyList<(string Code, string Name)> Seeds =
        [("G", "Grama"), ("ML", "Mililitro"), ("DZ", "Dúzia"), ("PCT", "Pacote"), ("POR", "Porção")];

    public string Key => BusinessProfileSeeds.InventoryFood;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IInventoryDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var seeded = (await db.Units.IgnoreQueryFilters().Where(u => u.SeedKey != null).Select(u => u.SeedKey!).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var codes = (await db.Units.Select(u => u.Code).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, name) in Seeds.Where(s => !seeded.Contains(SeedKeyOf(s.Code)) && !codes.Contains(s.Code)))
        {
            db.Units.Add(new MeasureUnit { SeedKey = SeedKeyOf(code), Code = code, Name = name, CreatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Chave do seed das unidades do segmento: o código em minúsculas, como as do <see cref="UnitSeeder"/>.</summary>
    public static string SeedKeyOf(string code) => code.ToLowerInvariant();
}
