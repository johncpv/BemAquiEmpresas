using BemAqui.Assets.Application;
using BemAqui.Assets.Contracts;
using BemAqui.Assets.Domain;
using BemAqui.BuildingBlocks.Attachments;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.HR.Contracts;
using BemAqui.Parties.Contracts;
using BemAqui.Production.Contracts;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Assets.Infrastructure;

public static class AssetsInfrastructure
{
    /// <summary>
    /// Patrimônio (PAT-01a): bens, locais com o seed, contrato <see cref="IAssets"/>, provedores de uso (terceiro, centro, funcionário),
    /// auditoria (o valor de aquisição no histórico só com <c>assets.edit</c>, CA 16) e os donos de anexo <c>AssetPhoto</c> e
    /// <c>Asset</c>, gravados só pela rota do bem (CA 8).
    /// </summary>
    public static IServiceCollection AddAssetsInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<AssetService>();
        services.TryAddScoped<AssetSiteService>();
        services.TryAddScoped<IAssets, AssetsContract>();
        services.TryAddScoped<AssetUsage>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPartyUsageProvider, AssetUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkCenterUsageProvider, AssetUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEmployeeUsageProvider, AssetUsage>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITenantSeeder, AssetSiteSeeder>());
        services.AddAuditableEntity<Asset>(AssetService.Module, "view", new Dictionary<string, string> { ["acquisitionValue"] = "edit" });
        services.AddAuditableEntity<AssetSite>(AssetService.Module);
        services.AddAttachmentOwner(AssetService.PhotoOwner, new AttachmentOwnerRules(AssetService.Module, ["image/png", "image/jpeg", "image/webp"], 5 * 1024 * 1024,
            OwnerExists: AssetExists, CanAttach: ByAssetOnly, StripImageMetadata: true));
        services.AddAttachmentOwner(AssetService.DocumentOwner, new AttachmentOwnerRules(AssetService.Module, ["application/pdf", "image/png", "image/jpeg"], 10 * 1024 * 1024,
            OwnerExists: AssetExists, CanAttach: ByAssetOnly, StripImageMetadata: true));
        return services;
    }

    private static async Task<bool> AssetExists(IServiceProvider services, Guid id, CancellationToken cancellationToken) =>
        await services.GetRequiredService<IAssetsDb>().Assets.AnyAsync(a => a.Id == id, cancellationToken);

    // A rota genérica de anexos não grava foto nem documento do bem: o envio é pela rota do bem, autorizado pelo registro (CA 8).
    private static Task<Error?> ByAssetOnly(IServiceProvider services, Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<Error?>(Error.Validation("attachment.by_asset_only", "Anexe a foto ou o documento pela ficha do bem."));
}

/// <summary>Locais "Fábrica" e "Escritório" (CA 5), idempotente pela <c>seed_key</c>.</summary>
public sealed class AssetSiteSeeder : ITenantSeeder
{
    public int Order => 60;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        await tenantScope.GetRequiredService<AssetSiteService>().SeedAsync(cancellationToken);
    }
}
