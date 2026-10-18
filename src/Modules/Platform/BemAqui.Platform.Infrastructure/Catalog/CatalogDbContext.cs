using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Platform.Application.Auth;
using BemAqui.Platform.Application.Modules;
using BemAqui.Platform.Application.Tenants;
using BemAqui.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Platform.Infrastructure.Catalog;

/// <summary>Banco de catálogo (schema <c>catalog</c>): tenants, domínios e planos (ADRs 0001 e 0004).</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options), IPlatformAuthDb, ITenantModulesDb, IPlatformTenantsDb
{
    public const string Schema = "catalog";
    public const string ChangeChannel = "tenant_changed";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<ProvisioningLog> ProvisioningLogs => Set<ProvisioningLog>();

    public DbSet<PlatformAlert> PlatformAlerts => Set<PlatformAlert>();

    public DbSet<PlanModule> PlanModules => Set<PlanModule>();

    public DbSet<TenantModule> TenantModules => Set<TenantModule>();

    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();

    public DbSet<PlatformAdminToken> PlatformAdminTokens => Set<PlatformAdminToken>();

    public DbSet<PlatformRecoveryCode> PlatformRecoveryCodes => Set<PlatformRecoveryCode>();

    public DbSet<PlatformSession> PlatformSessions => Set<PlatformSession>();

    /// <summary>Auditoria das alterações do catálogo (ex.: suspensão de tenant), imutável como a dos tenants.</summary>
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Plan>(b =>
        {
            b.ToTable("plans");
            b.Property(e => e.Name).HasMaxLength(100);
            b.Property(e => e.Limits).HasColumnType("jsonb");
            b.Property(e => e.Price).HasPrecision(18, 2);
        });

        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.Property(e => e.Key).HasMaxLength(63);
            b.HasIndex(e => e.Key).IsUnique();
            b.Property(e => e.LegalName).HasMaxLength(200);
            b.Property(e => e.TradeName).HasMaxLength(200);
            b.Property(e => e.Document).HasMaxLength(18);
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(e => e.DbName).HasMaxLength(63);
            b.Property(e => e.SchemaVersion).HasMaxLength(150);
            b.HasOne<Plan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(e => e.Domains).WithOne().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditEntry>(b =>
        {
            b.ToTable("audit_log", "audit");
            b.HasKey(e => e.Id);
            b.Property(e => e.Entity).HasMaxLength(128);
            b.Property(e => e.EntityId).HasMaxLength(64);
            b.Property(e => e.Action).HasMaxLength(64);
            b.Property(e => e.Changes).HasColumnType("jsonb");
            b.Property(e => e.Ip).HasMaxLength(64);
            b.Property(e => e.Origin).HasMaxLength(300);
            b.HasIndex(e => new { e.Entity, e.EntityId, e.At });
        });

        modelBuilder.Entity<PlatformAlert>(b =>
        {
            b.ToTable("platform_alerts");
            b.Property(e => e.Key).HasMaxLength(200);
            b.HasIndex(e => e.Key).IsUnique();
            b.Property(e => e.LastSummary).HasMaxLength(500);
        });

        modelBuilder.Entity<ProvisioningLog>(b =>
        {
            b.ToTable("provisioning_logs");
            b.Property(e => e.Step).HasMaxLength(32);
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(e => e.Message).HasMaxLength(2000);
            b.HasIndex(e => new { e.TenantId, e.At });
            b.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlanModule>(b =>
        {
            b.ToTable("plan_modules");
            b.Property(e => e.ModuleCode).HasMaxLength(32);
            b.HasIndex(e => new { e.PlanId, e.ModuleCode }).IsUnique();
            b.HasOne<Plan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TenantModule>(b =>
        {
            b.ToTable("tenant_modules");
            b.Property(e => e.ModuleCode).HasMaxLength(32);
            b.HasIndex(e => new { e.TenantId, e.ModuleCode }).IsUnique();
            b.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlatformAdmin>(b =>
        {
            b.ToTable("platform_admins");
            b.Property(e => e.Email).HasMaxLength(320);
            b.Property(e => e.NormalizedEmail).HasMaxLength(320);
            b.HasIndex(e => e.NormalizedEmail).IsUnique();
            b.Property(e => e.PasswordHash).HasMaxLength(512);
            b.Property(e => e.SecurityStamp).HasMaxLength(64);
            b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(e => e.TotpSecretCipher).HasMaxLength(1024);
        });

        modelBuilder.Entity<PlatformAdminToken>(b =>
        {
            b.ToTable("platform_admin_tokens");
            b.Property(e => e.TokenHash).HasMaxLength(64);
            b.HasIndex(e => e.TokenHash).IsUnique();
            b.Property(e => e.Purpose).HasConversion<string>().HasMaxLength(16);
            b.HasIndex(e => e.AdminId);
            b.HasOne<PlatformAdmin>().WithMany().HasForeignKey(e => e.AdminId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlatformRecoveryCode>(b =>
        {
            b.ToTable("platform_recovery_codes");
            b.Property(e => e.CodeHash).HasMaxLength(64);
            b.HasIndex(e => new { e.AdminId, e.CodeHash });
            b.HasOne<PlatformAdmin>().WithMany().HasForeignKey(e => e.AdminId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlatformSession>(b =>
        {
            b.ToTable("platform_sessions");
            b.Property(e => e.TokenHash).HasMaxLength(64);
            b.HasIndex(e => e.TokenHash).IsUnique();
            b.Property(e => e.Host).HasMaxLength(253);
            b.Property(e => e.CreatedByIp).HasMaxLength(64);
            b.HasIndex(e => e.AdminId);
            b.HasOne<PlatformAdmin>().WithMany().HasForeignKey(e => e.AdminId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TenantDomain>(b =>
        {
            b.ToTable("tenant_domains");
            b.Property(e => e.Host).HasMaxLength(253);
            b.HasIndex(e => e.Host).IsUnique();
        });
    }
}
