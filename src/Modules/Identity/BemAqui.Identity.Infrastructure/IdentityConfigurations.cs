using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Identity.Infrastructure;

internal static class IdentitySchema
{
    public const string Name = "identity";
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users", IdentitySchema.Name);
        b.Property(e => e.Name).HasMaxLength(200);
        b.Property(e => e.Email).HasMaxLength(320);
        b.Property(e => e.NormalizedEmail).HasMaxLength(320);
        b.HasIndex(e => e.NormalizedEmail).IsUnique();
        b.Property(e => e.PasswordHash).HasMaxLength(512);
        b.Property(e => e.SecurityStamp).HasMaxLength(64);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.TotpSecretCipher).HasMaxLength(1024);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens", IdentitySchema.Name);
        b.Property(e => e.TokenHash).HasMaxLength(64);
        b.HasIndex(e => e.TokenHash).IsUnique();
        b.HasIndex(e => e.FamilyId);
        b.HasIndex(e => new { e.UserId, e.RevokedAt });
        b.Property(e => e.CreatedByIp).HasMaxLength(64);
        b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserInviteConfiguration : IEntityTypeConfiguration<UserInvite>
{
    public void Configure(EntityTypeBuilder<UserInvite> b)
    {
        b.ToTable("user_invites", IdentitySchema.Name);
        b.Property(e => e.Email).HasMaxLength(320);
        b.Property(e => e.TokenHash).HasMaxLength(64);
        b.HasIndex(e => e.TokenHash);
        b.HasIndex(e => e.UserId);
        b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PasswordResetConfiguration : IEntityTypeConfiguration<PasswordReset>
{
    public void Configure(EntityTypeBuilder<PasswordReset> b)
    {
        b.ToTable("password_resets", IdentitySchema.Name);
        b.Property(e => e.TokenHash).HasMaxLength(64);
        b.HasIndex(e => e.TokenHash).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RecoveryCodeConfiguration : IEntityTypeConfiguration<RecoveryCode>
{
    public void Configure(EntityTypeBuilder<RecoveryCode> b)
    {
        b.ToTable("recovery_codes", IdentitySchema.Name);
        b.Property(e => e.CodeHash).HasMaxLength(64);
        b.HasIndex(e => new { e.UserId, e.CodeHash });
        b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TwoFactorChallengeConfiguration : IEntityTypeConfiguration<TwoFactorChallenge>
{
    public void Configure(EntityTypeBuilder<TwoFactorChallenge> b)
    {
        b.ToTable("two_factor_challenges", IdentitySchema.Name);
        b.Property(e => e.TokenHash).HasMaxLength(64);
        b.HasIndex(e => e.TokenHash).IsUnique();
        b.Property(e => e.Purpose).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.CreatedByIp).HasMaxLength(64);
        b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> b)
    {
        b.ToTable("tenant_settings", IdentitySchema.Name);
        b.Property(e => e.Key).HasMaxLength(100);
        b.HasIndex(e => e.Key).IsUnique();
        b.Property(e => e.Value).HasMaxLength(4000);
    }
}

internal sealed class CatalogModuleConfiguration : IEntityTypeConfiguration<CatalogModule>
{
    public void Configure(EntityTypeBuilder<CatalogModule> b)
    {
        b.ToTable("modules", IdentitySchema.Name);
        b.Property(e => e.Code).HasMaxLength(32);
        b.HasIndex(e => e.Code).IsUnique();
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.Area).HasMaxLength(32);
        b.Property(e => e.Description).HasMaxLength(500);
        b.Property(e => e.ApproveMeaning).HasMaxLength(200);
    }
}

internal sealed class CatalogModuleActionConfiguration : IEntityTypeConfiguration<CatalogModuleAction>
{
    public void Configure(EntityTypeBuilder<CatalogModuleAction> b)
    {
        b.ToTable("module_actions", IdentitySchema.Name);
        b.Property(e => e.ModuleCode).HasMaxLength(32);
        b.Property(e => e.Action).HasMaxLength(32);
        b.HasIndex(e => new { e.ModuleCode, e.Action }).IsUnique();
        b.Property(e => e.Label).HasMaxLength(100);
        b.Property(e => e.Description).HasMaxLength(500);
    }
}

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> b)
    {
        b.ToTable("groups", IdentitySchema.Name);
        b.Property(e => e.Name).HasMaxLength(100);
        b.HasIndex(e => e.Name).IsUnique().HasFilter("is_active");
        b.Property(e => e.Description).HasMaxLength(500);
        b.Property(e => e.SeedKey).HasMaxLength(32);
        b.HasIndex(e => e.SeedKey).IsUnique();
        b.Ignore(e => e.IsOwner);
    }
}

internal sealed class GroupModuleConfiguration : IEntityTypeConfiguration<GroupModule>
{
    public void Configure(EntityTypeBuilder<GroupModule> b)
    {
        b.ToTable("group_modules", IdentitySchema.Name);
        b.Property(e => e.ModuleCode).HasMaxLength(32);
        b.HasIndex(e => new { e.GroupId, e.ModuleCode }).IsUnique();
        b.HasOne<Group>().WithMany().HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GroupGrantConfiguration : IEntityTypeConfiguration<GroupGrant>
{
    public void Configure(EntityTypeBuilder<GroupGrant> b)
    {
        b.ToTable("group_permissions", IdentitySchema.Name);
        b.Property(e => e.ModuleCode).HasMaxLength(32);
        b.Property(e => e.Action).HasMaxLength(32);
        b.HasIndex(e => new { e.GroupId, e.ModuleCode, e.Action }).IsUnique();
        b.HasOne<Group>().WithMany().HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserGroupConfiguration : IEntityTypeConfiguration<UserGroup>
{
    public void Configure(EntityTypeBuilder<UserGroup> b)
    {
        b.ToTable("user_groups", IdentitySchema.Name);
        b.HasIndex(e => new { e.UserId, e.GroupId }).IsUnique();
        b.HasIndex(e => e.GroupId);
        b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Group>().WithMany().HasForeignKey(e => e.GroupId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> b)
    {
        b.ToTable("user_preferences", IdentitySchema.Name);
        b.Property(e => e.Key).HasMaxLength(64);
        b.Property(e => e.Value).HasMaxLength(4000);
        b.HasIndex(e => new { e.UserId, e.Key }).IsUnique();
        b.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AiSettingsConfiguration : IEntityTypeConfiguration<AiSettings>
{
    public void Configure(EntityTypeBuilder<AiSettings> b)
    {
        b.ToTable("ai_settings", IdentitySchema.Name);
        b.Property(e => e.Provider).HasMaxLength(20);
        b.Property(e => e.Model).HasMaxLength(100);
        b.Property(e => e.ApiKeyCipher).HasMaxLength(2000);
    }
}

internal sealed class AiUsageLogConfiguration : IEntityTypeConfiguration<AiUsageLog>
{
    public void Configure(EntityTypeBuilder<AiUsageLog> b)
    {
        b.ToTable("ai_usage_logs", IdentitySchema.Name);
        b.Property(e => e.Provider).HasMaxLength(20);
        b.Property(e => e.Model).HasMaxLength(100);
        b.Property(e => e.Outcome).HasMaxLength(20);
        b.HasIndex(e => e.At);
    }
}
