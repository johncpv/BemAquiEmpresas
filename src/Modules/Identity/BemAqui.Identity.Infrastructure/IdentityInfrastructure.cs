using Microsoft.EntityFrameworkCore;
using BemAqui.BuildingBlocks.Attachments;

using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Application;
using BemAqui.Identity.Application.Ai;
using BemAqui.Identity.Infrastructure.Ai;
using BemAqui.Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        // 2FA de novo antes de ação sensível (D6): o webhook da FND-16 e, na BNK-01a, as credenciais do Inter.
        services.AddScoped<BemAqui.BuildingBlocks.Identity.IStepUpTwoFactor>(sp => sp.GetRequiredService<TwoFactorService>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Tenancy.ITenantSeeder, ModuleCatalogSeeder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Tenancy.ITenantSeeder, GroupSeeder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Tenancy.ITenantSeeder, SeedPatchSeeder>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITwoFactorRequirementRule, GroupRequiresTwoFactorRule>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ITwoFactorRequirementRule, ApproverRequiresTwoFactorRule>());
        services.TryAddScoped<BemAqui.BuildingBlocks.Identity.IPermissionService, PermissionService>();
        services.TryAddScoped<GroupService>();
        services.TryAddScoped<BemAqui.BuildingBlocks.Identity.IUserDirectory, UserDirectory>();
        services.TryAddScoped<UserService>();
        services.TryAddScoped<SecuritySettings>();
        services.TryAddScoped<Preferences>();
        services.TryAddScoped<Profile>();
        services.TryAddScoped<HelpAccess>();
        AddAi(services);

        // Permissão real no lugar do DenyAll e entidades com histórico consultável (FND-07 + FND-06c).
        services.AddScoped<BemAqui.BuildingBlocks.Authorization.IPermissionChecker, CurrentUserPermissionChecker>();
        services.AddAuditableEntity<Group>("identity");
        services.AddAuditableEntity<User>("identity");
        services.AddAuditableEntity<TenantSetting>("settings");
        // Configurações da empresa (CAD-06): perfil, logo, numeração e parâmetros.
        services.TryAddScoped<CompanySettings>();
        services.TryAddScoped<BemAqui.BuildingBlocks.Tenancy.ITenantBranding>(sp => sp.GetRequiredService<CompanySettings>());
        services.TryAddScoped<BemAqui.Identity.Contracts.ICompanyIdentity>(sp => sp.GetRequiredService<CompanySettings>());
        services.TryAddScoped<BemAqui.Identity.Contracts.ICompanyFiscalProfile>(sp => sp.GetRequiredService<CompanySettings>());
        services.TryAddScoped<TenantParametersService>();
        services.TryAddScoped<BemAqui.Identity.Contracts.ITenantParameters>(sp => sp.GetRequiredService<TenantParametersService>());
        services.TryAddScoped<BemAqui.Identity.Contracts.IUserEmployeeLink, UserEmployeeLink>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Tenancy.ITenantSeeder, TenantParametersSeeder>());
        services.AddAuditableEntity<CompanyProfile>("settings");
        services.AddAuditableEntity<BemAqui.BuildingBlocks.Persistence.Numbering.NumberFormatOverride>("settings");
        services.AddAttachmentOwner(CompanyProfile.LogoOwnerType, new AttachmentOwnerRules("settings", CompanySettings.LogoTypes, CompanySettings.LogoMaxSize, CompanySettings.LogoMaxSide,
            async (sp, id, ct) => await sp.GetRequiredService<IIdentityDb>().CompanyProfiles.AnyAsync(p => p.Id == id, ct)));
        services.AddAuditableEntity<AiSettings>("settings");
        services.TryAddScoped<BemAqui.Platform.Contracts.ITenantOwnerOnboarding, IdentityOwnerOnboarding>();
        // Chão de fábrica (PRD-03b, ADR 0026): aparelho registrado, PIN e sessão por PIN conferida a cada request.
        services.TryAddScoped<Application.Shopfloor.ShopfloorRevocations>();
        services.TryAddScoped<Application.Shopfloor.ShopfloorDeviceGate>();
        services.TryAddScoped<Application.Shopfloor.ShopfloorDeviceService>();
        services.TryAddScoped<Application.Shopfloor.PinService>();
        services.TryAddScoped<BemAqui.BuildingBlocks.Identity.IPinSessionValidator, Application.Shopfloor.PinSessionValidator>();
        services.AddAuditableEntity<ShopfloorDevice>("settings");
        services.AddAuditableEntity<UserPin>("identity");
        // Painel da TV (PNL-01a, ADR 0030): telas com token de uso único, sessão de quiosque conferida a cada request e a troca da rota.
        services.TryAddSingleton<Application.Kiosk.KioskSessionVersions>();
        services.TryAddSingleton<BemAqui.BuildingBlocks.Identity.IKioskRealtime, BemAqui.BuildingBlocks.Identity.NoKioskRealtime>();
        services.TryAddScoped<Application.Kiosk.KioskNotices>();
        services.TryAddScoped<Application.Kiosk.KioskScreenService>();
        services.TryAddScoped<Application.Kiosk.KioskSessionService>();
        services.TryAddScoped<BemAqui.BuildingBlocks.Identity.IKioskSessionValidator>(sp => sp.GetRequiredService<Application.Kiosk.KioskSessionService>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<BemAqui.BuildingBlocks.Tenancy.ITenantSeeder, KioskLegacyRouteSeeder>());
        services.AddAuditableEntity<KioskScreen>("kiosk");
        return services;
    }

    /// <summary>Pergunte ao BemAqui (FND-15): um HttpClient por provedor, sem log de requisição (o corpo leva a pergunta).</summary>
    private static void AddAi(IServiceCollection services)
    {
        services.AddOptions<AiOptions>().BindConfiguration(AiOptions.Section);
        services.AddHttpClient<AnthropicProvider>((sp, c) => c.BaseAddress = sp.GetRequiredService<IOptions<AiOptions>>().Value.AnthropicBaseUrl).RemoveAllLoggers();
        services.AddHttpClient<GeminiProvider>((sp, c) => c.BaseAddress = sp.GetRequiredService<IOptions<AiOptions>>().Value.GeminiBaseUrl).RemoveAllLoggers();
        services.AddHttpClient<OpenAiProvider>((sp, c) => c.BaseAddress = sp.GetRequiredService<IOptions<AiOptions>>().Value.OpenAiBaseUrl).RemoveAllLoggers();
        services.AddTransient<IAiProvider>(sp => sp.GetRequiredService<AnthropicProvider>());
        services.AddTransient<IAiProvider>(sp => sp.GetRequiredService<GeminiProvider>());
        services.AddTransient<IAiProvider>(sp => sp.GetRequiredService<OpenAiProvider>());
        services.TryAddScoped<AiSettingsService>();
        services.TryAddScoped<HelpAnswerService>();
    }
}
