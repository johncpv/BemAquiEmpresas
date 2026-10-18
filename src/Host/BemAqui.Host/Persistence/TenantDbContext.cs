using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Host.Persistence;

/// <summary>
/// DbContext único do banco de tenant (ADR 0006): tabelas compartilhadas da base e, conforme as stories
/// avançam, as configurações de cada módulo. A conexão por tenant chega na FND-02.
/// </summary>
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : BemAquiDbContext(options), IIdentityDb
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserInvite> UserInvites => Set<UserInvite>();

    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public DbSet<TwoFactorChallenge> TwoFactorChallenges => Set<TwoFactorChallenge>();

    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

    public DbSet<CatalogModule> CatalogModules => Set<CatalogModule>();

    public DbSet<CatalogModuleAction> CatalogModuleActions => Set<CatalogModuleAction>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupModule> GroupModules => Set<GroupModule>();

    public DbSet<GroupGrant> GroupPermissions => Set<GroupGrant>();

    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    protected override void ConfigureModules(ModelBuilder modelBuilder)
    {
        foreach (var module in ModuleRegistry.All)
        {
            if (module.PersistenceAssembly is { } assembly)
            {
                modelBuilder.ApplyConfigurationsFromAssembly(assembly);
            }
        }
    }
}
