using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Identity.Infrastructure;

/// <summary>Tela de TV (<c>identity.kiosk_tokens</c>, ADR 0030): só o hash do token (único) e o prefixo; escopo em colunas próprias.</summary>
internal sealed class KioskScreenConfiguration : IEntityTypeConfiguration<KioskScreen>
{
    public void Configure(EntityTypeBuilder<KioskScreen> b)
    {
        b.ToTable("kiosk_tokens", IdentitySchema.Name);
        b.Property(e => e.Name).HasMaxLength(KioskScreen.MaxNameLength);
        b.Property(e => e.TokenHash).HasMaxLength(64);
        b.HasIndex(e => e.TokenHash).IsUnique();
        b.Property(e => e.TokenPrefix).HasMaxLength(KioskScreen.PrefixLength);
        b.Property(e => e.Line).HasMaxLength(KioskScreen.MaxLineLength);
        b.Property(e => e.Views).HasColumnType("text[]");
        b.Property(e => e.AllowedNetworks).HasColumnType("text[]");
        b.HasIndex(e => e.RevokedAt);
        b.Ignore(e => e.Status);
        b.Ignore(e => e.IsRevoked);
    }
}

/// <summary>Sessão de quiosque (<c>identity.kiosk_sessions</c>): só o hash do cookie (único); no máximo uma ativa por tela.</summary>
internal sealed class KioskSessionConfiguration : IEntityTypeConfiguration<KioskSession>
{
    public void Configure(EntityTypeBuilder<KioskSession> b)
    {
        b.ToTable("kiosk_sessions", IdentitySchema.Name);
        b.Property(e => e.TokenHash).HasMaxLength(64);
        b.HasIndex(e => e.TokenHash).IsUnique();
        b.Property(e => e.Ip).HasMaxLength(64);
        b.HasIndex(e => e.KioskId).HasFilter("revoked_at IS NULL").HasDatabaseName("ix_kiosk_sessions_active");
        b.HasOne<KioskScreen>().WithMany().HasForeignKey(e => e.KioskId).OnDelete(DeleteBehavior.Restrict);
    }
}
