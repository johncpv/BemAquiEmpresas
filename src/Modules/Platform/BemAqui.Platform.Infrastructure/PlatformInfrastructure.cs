using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.Platform.Application.Auth;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Alerts;
using BemAqui.Platform.Infrastructure.Catalog;
using BemAqui.Platform.Infrastructure.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Platform.Infrastructure;

public static class PlatformInfrastructure
{
    /// <summary>Catálogo (banco <c>catalog</c>), resolução de tenant com cache e invalidação por LISTEN/NOTIFY.</summary>
    public static IServiceCollection AddPlatformInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var catalog = configuration.GetConnectionString("Catalog")
            ?? throw new InvalidOperationException("ConnectionStrings:Catalog não configurada.");

        services.AddDbContext<CatalogDbContext>((sp, o) =>
            CatalogOptions.Configure((DbContextOptionsBuilder<CatalogDbContext>)o, catalog)
                .AddInterceptors(sp.GetRequiredService<BuildingBlocks.Persistence.Auditing.AuditSaveChangesInterceptor>()));
        services.AddBemAquiPersistenceCore();

        services.TryAddSingleton<CatalogTenantResolver>();
        services.TryAddSingleton<ITenantResolver>(sp => sp.GetRequiredService<CatalogTenantResolver>());
        services.TryAddSingleton<ITenantDirectory>(sp => sp.GetRequiredService<CatalogTenantResolver>());
        services.TryAddScoped<TenantContextHolder>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContextHolder>());
        services.TryAddSingleton<ITenantScopeFactory, TenantScopeFactory>();

        services.TryAddScoped<IProvisioningCatalog, EfProvisioningCatalog>();
        services.TryAddSingleton<ITenantDatabaseAdmin, PostgresTenantDatabaseAdmin>();
        services.TryAddScoped<ProvisioningService>();

        // Login do super admin (FND-13).
        services.Configure<PlatformAuthOptions>(configuration.GetSection(PlatformAuthOptions.Section));
        services.TryAddScoped<IPlatformAuthDb>(sp => sp.GetRequiredService<CatalogDbContext>());
        services.TryAddSingleton<Microsoft.AspNetCore.Identity.IPasswordHasher<PlatformAdmin>, Microsoft.AspNetCore.Identity.PasswordHasher<PlatformAdmin>>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<PlatformAuthService>();

        services.Configure<AlertOptions>(configuration.GetSection(AlertOptions.Section));
        services.AddSingleton<IAlertSink, CatalogAlertSink>(); // substitui o LogOnlyAlertSink padrão
        services.TryAddSingleton<IntegrationHealthTracker>();

        services.AddHostedService<CatalogMigrationService>();
        services.AddHostedService<CatalogChangeListener>();
        return services;
    }
}
