using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Domain;
using BemAqui.Parties.Application;
using BemAqui.Parties.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Host.Persistence;

/// <summary>
/// DbContext único do banco de tenant (ADR 0006): tabelas compartilhadas da base e, conforme as stories
/// avançam, as configurações de cada módulo. A conexão por tenant chega na FND-02.
/// </summary>
public sealed class TenantDbContext(DbContextOptions<TenantDbContext> options) : BemAquiDbContext(options), IIdentityDb, IPartiesDb, IMigrationDb, IFinanceDb
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserInvite> UserInvites => Set<UserInvite>();

    public DbSet<PasswordReset> PasswordResets => Set<PasswordReset>();

    public DbSet<RecoveryCode> RecoveryCodes => Set<RecoveryCode>();

    public DbSet<TwoFactorChallenge> TwoFactorChallenges => Set<TwoFactorChallenge>();

    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();

    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();

    public DbSet<CatalogModule> CatalogModules => Set<CatalogModule>();

    public DbSet<CatalogModuleAction> CatalogModuleActions => Set<CatalogModuleAction>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupModule> GroupModules => Set<GroupModule>();

    public DbSet<GroupGrant> GroupPermissions => Set<GroupGrant>();

    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    public DbSet<AiSettings> AiSettings => Set<AiSettings>();

    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();

    public DbSet<SeedPatch> SeedPatches => Set<SeedPatch>();

    public DbSet<Party> Parties => Set<Party>();

    public DbSet<PartyRole> PartyRoles => Set<PartyRole>();

    public DbSet<PartyAddress> PartyAddresses => Set<PartyAddress>();

    public DbSet<PartyContact> PartyContacts => Set<PartyContact>();

    public DbSet<CnpjLookupCache> CnpjLookups => Set<CnpjLookupCache>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<PaymentTerm> PaymentTerms => Set<PaymentTerm>();

    public DbSet<ImportRun> ImportRuns => Set<ImportRun>();

    public DbSet<ImportItem> ImportItems => Set<ImportItem>();

    public DbSet<ExternalRef> ExternalRefs => Set<ExternalRef>();

    protected override void ConfigureModules(ModelBuilder modelBuilder)
    {
        // Busca por parte do nome e do CNPJ (CAD-01a CA 18). pg_trgm é extensão confiável: o dono do banco cria.
        modelBuilder.HasPostgresExtension("pg_trgm");
        foreach (var module in ModuleRegistry.All)
        {
            if (module.PersistenceAssembly is { } assembly)
            {
                modelBuilder.ApplyConfigurationsFromAssembly(assembly);
            }
        }
    }
}
