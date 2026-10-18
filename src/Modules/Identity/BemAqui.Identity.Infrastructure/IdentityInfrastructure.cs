
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BemAqui.Identity.Infrastructure;

public static class IdentityInfrastructure
{
    /// <summary>Hash PBKDF2 do ASP.NET Core Identity, emissão de JWT e validação do security stamp (FND-04a).</summary>
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.TryAddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.TryAddScoped<BemAqui.BuildingBlocks.Identity.ISecurityStampValidator, CachedSecurityStampValidator>();
        services.TryAddScoped<ISecurityStampCache>(sp => (CachedSecurityStampValidator)sp.GetRequiredService<BemAqui.BuildingBlocks.Identity.ISecurityStampValidator>());
        services.TryAddScoped<AuthService>();
        services.TryAddScoped<UserLifecycle>();
        services.TryAddScoped<AccountRecovery>();
        services.TryAddScoped<Invites>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITwoFactorRequirementRule, TenantRequiresTwoFactorRule>());
        services.TryAddScoped<TwoFactorRequirement>();
        services.TryAddScoped<TwoFactorService>();
        services.TryAddScoped<BemAqui.Platform.Contracts.ITenantOwnerOnboarding, IdentityOwnerOnboarding>();
        return services;
    }
}
