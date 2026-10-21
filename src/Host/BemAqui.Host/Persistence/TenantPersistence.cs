using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;

namespace BemAqui.Host.Persistence;

public static class TenantPersistence
{
    /// <summary>
    /// TenantDbContext com a conexão do tenant do escopo, decifrada só aqui (FND-11). Sem tenant no escopo
    /// (request sem host resolvido, job sem TenantJob) a criação falha: nunca cai num banco padrão.
    /// </summary>
    public static IServiceCollection AddTenantPersistence(this IServiceCollection services)
    {
        services.AddDbContext<TenantDbContext>((sp, options) =>
        {
            var tenant = sp.GetRequiredService<TenantContextHolder>().Resolved
                ?? throw new InvalidOperationException("TenantDbContext usado fora de um escopo de tenant.");
            var connection = sp.GetRequiredService<ISecretProtector>()
                .Unprotect(SecretScope.ForTenant(tenant.Tenant.Id), SecretKind.TenantConnection, tenant.ConnectionCipher);
            options.UseBemAquiDefaults(connection.Reveal(), sp);
        });
        services.AddScoped<BemAquiDbContext>(sp => sp.GetRequiredService<TenantDbContext>());
        services.AddScoped<BemAqui.Identity.Application.IIdentityDb>(sp => sp.GetRequiredService<TenantDbContext>());
        services.AddScoped<BemAqui.Parties.Application.IPartiesDb>(sp => sp.GetRequiredService<TenantDbContext>());
        services.AddScoped<BemAqui.Finance.Application.IFinanceDb>(sp => sp.GetRequiredService<TenantDbContext>());
        services.AddScoped<BemAqui.Inventory.Application.IInventoryDb>(sp => sp.GetRequiredService<TenantDbContext>());
        services.AddScoped<BemAqui.Production.Application.IProductionDb>(sp => sp.GetRequiredService<TenantDbContext>());
        services.AddScoped<BemAqui.HR.Application.IHrDb>(sp => sp.GetRequiredService<TenantDbContext>());
        services.AddScoped<BemAqui.Engineering.Application.IEngineeringDb>(sp => sp.GetRequiredService<TenantDbContext>());
        services.AddScoped<BemAqui.Commercial.Application.ICommercialDb>(sp => sp.GetRequiredService<TenantDbContext>());
        services.AddScoped<BemAqui.Migrations.Application.IMigrationDb>(sp => sp.GetRequiredService<TenantDbContext>());
        return services;
    }
}
