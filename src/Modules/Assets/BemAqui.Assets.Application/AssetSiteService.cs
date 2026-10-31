using BemAqui.Assets.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Assets.Application;

/// <summary>
/// Locais do patrimônio (<c>assets.sites</c>, CA 5, ADR 0038 §2): cadastro simples de setores, nome único entre os não excluídos,
/// seed "Fábrica" e "Escritório". Com bem, o local não é excluído, só inativado (ADR 0016).
/// </summary>
public sealed class AssetSiteService(IAssetsDb db, TimeProvider clock)
{
    public const string FactorySeedKey = "factory";
    public const string OfficeSeedKey = "office";
    public const string InUseCode = "asset_site_in_use";
    private const int NameMax = 100;

    private static readonly (string Key, string Name)[] Seeds = [(FactorySeedKey, "Fábrica"), (OfficeSeedKey, "Escritório")];

    public async Task<IReadOnlyList<AssetSiteView>> ListAsync(bool includeInactive, CancellationToken cancellationToken = default)
    {
        var sites = await db.AssetSites.AsNoTracking().Where(s => includeInactive || s.IsActive).OrderBy(s => s.Name).ToListAsync(cancellationToken);
        var ids = sites.Select(s => s.Id).ToList();
        var counts = await db.Assets.AsNoTracking().Where(a => a.SiteId != null && ids.Contains(a.SiteId.Value))
            .GroupBy(a => a.SiteId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return sites.Select(s => View(s, counts.GetValueOrDefault(s.Id))).ToList();
    }

    /// <summary>Criação, também a rápida do combobox do bem (CA 5, com <c>assets.edit</c>).</summary>
    public async Task<Result<AssetSiteView>> CreateAsync(AssetSiteInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var site = new AssetSite { CreatedAt = clock.GetUtcNow() };
        if (await ApplyNameAsync(site, input.Name, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        db.AssetSites.Add(site);
        await db.SaveChangesAsync(cancellationToken);
        return View(site, 0);
    }

    public async Task<Result<AssetSiteView>> UpdateAsync(Guid id, AssetSiteInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var site = await db.AssetSites.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (site is null)
        {
            return NotFound();
        }

        if (await ApplyNameAsync(site, input.Name, cancellationToken) is { } invalid)
        {
            return invalid;
        }

        if (input.IsActive is { } active)
        {
            site.IsActive = active;
        }

        await db.SaveChangesAsync(cancellationToken);
        return View(site, await db.Assets.CountAsync(a => a.SiteId == id, cancellationToken));
    }

    /// <summary>Excluir (lógico) só o local sem bens; com bem, 409 e a sugestão de inativar.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var site = await db.AssetSites.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (site is null)
        {
            return NotFound();
        }

        if (await db.Assets.AnyAsync(a => a.SiteId == id, cancellationToken))
        {
            return Error.Conflict(InUseCode, "Local com bens não pode ser excluído. Inative o local ou mude os bens de local.");
        }

        db.AssetSites.Remove(site);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Seed "Fábrica" e "Escritório" (CA 5): uma vez por <c>seed_key</c>; o que o tenant excluiu ou renomeou não volta.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await db.AssetSites.IgnoreQueryFilters().AsNoTracking().Select(s => new { s.SeedKey, s.Name, s.IsDeleted }).ToListAsync(cancellationToken);
        foreach (var (key, name) in Seeds)
        {
            if (existing.Any(s => s.SeedKey == key) || existing.Any(s => !s.IsDeleted && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            db.AssetSites.Add(new AssetSite { Name = name, SeedKey = key, CreatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Error?> ApplyNameAsync(AssetSite site, string? input, CancellationToken ct)
    {
        var name = AssetRules.Clean(input);
        if (name is null || name.Length > NameMax)
        {
            return AssetService.Invalid("name", "Informe o nome do local, com até 100 caracteres.");
        }

        if (await db.AssetSites.AnyAsync(s => s.Id != site.Id && EF.Functions.ILike(s.Name, AssetRules.LikeExact(name)), ct))
        {
            return Error.Conflict("asset_site_name_taken", $"Já existe o local {name}.") with
            {
                Fields = new Dictionary<string, string[]> { ["name"] = [$"Já existe o local {name}."] },
            };
        }

        site.Name = name;
        return null;
    }

    private static AssetSiteView View(AssetSite s, int assets) => new(s.Id, s.Name, s.IsActive, s.SeedKey is not null, assets);

    private static Error NotFound() => Error.NotFound("asset_site_not_found", "Local não encontrado.");
}
