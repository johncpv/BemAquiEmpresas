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
