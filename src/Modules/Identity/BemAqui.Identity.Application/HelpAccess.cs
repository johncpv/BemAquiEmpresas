using BemAqui.BuildingBlocks.Help;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

/// <summary>
/// Ajuda filtrada pela permissão efetiva (FND-14 CA 2 e 3, R-AJD-6): geral (sem módulo) para todos; de módulo, só se o
/// módulo estiver liberado e habilitado para o usuário. Ajudas da plataforma não aparecem nos hosts de tenant.
/// </summary>
public sealed class HelpAccess(HelpCatalog catalog, IPermissionService permissions, TenantContextHolder tenant, IIdentityDb db, TimeProvider clock)
{
    public const string SeenPrefix = "help:";

    public async Task<IReadOnlyList<HelpDocument>> IndexAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var effective = await permissions.GetAsync(userId, cancellationToken);
        return catalog.Documents.Where(d => Check(d, effective) is null).ToList();
    }

    public async Task<Result<HelpDocument>> GetAsync(Guid userId, string key, CancellationToken cancellationToken = default)
    {
        var document = catalog.Find(key);
        if (document is null || IsPlatform(document))
        {
            return Error.NotFound(Error.Codes.NotFound, "Ajuda não encontrada.");
        }

        var denied = Check(document, await permissions.GetAsync(userId, cancellationToken));
        return denied is null ? document : denied;
    }

    /// <summary>Ajudas já vistas (CA 6): ficam no perfil, então valem em qualquer dispositivo.</summary>
    public async Task<IReadOnlyList<string>> SeenAsync(Guid userId, CancellationToken cancellationToken = default) =>
        (await db.UserPreferences.Where(p => p.UserId == userId && p.Key.StartsWith(SeenPrefix)).Select(p => p.Key).ToListAsync(cancellationToken))
            .Select(k => k[SeenPrefix.Length..]).ToList();

    public async Task MarkSeenAsync(Guid userId, string key, CancellationToken cancellationToken = default)
    {
        if (catalog.Find(key) is null || await db.UserPreferences.AnyAsync(p => p.UserId == userId && p.Key == SeenPrefix + key, cancellationToken))
        {
            return;
        }

        db.UserPreferences.Add(new UserPreference { UserId = userId, Key = SeenPrefix + key, Value = clock.GetUtcNow().ToString("O"), UpdatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsPlatform(HelpDocument document) => document.Module == "plataforma";

    private Error? Check(HelpDocument document, EffectivePermissions effective)
    {
        if (IsPlatform(document))
        {
            return Error.NotFound(Error.Codes.NotFound, "Ajuda não encontrada.");
        }

        if (document.Module is not { } module)
        {
            return null;
        }

        if (tenant.Resolved?.IsReleased(module) != true || ModuleCatalog.Find(module) is null)
        {
            return Error.Forbidden(Error.Codes.ModuleNotReleased, "Funcionalidade não liberada nessa versão do sistema.");
        }

        return effective.Permissions.Any(p => p.StartsWith(module + ".", StringComparison.Ordinal))
            ? null
            : Error.Forbidden(Error.Codes.Forbidden, "Você não tem acesso a esta funcionalidade. Fale com o administrador.");
    }
}
