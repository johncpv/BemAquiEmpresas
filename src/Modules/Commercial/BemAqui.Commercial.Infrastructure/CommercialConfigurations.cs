using BemAqui.Commercial.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Commercial.Infrastructure;

internal static class CommercialSchema
{
    public const string Name = "commercial";
}

internal sealed class QuoteConfiguration : IEntityTypeConfiguration<Quote>
{
    public void Configure(EntityTypeBuilder<Quote> b)
    {
        b.ToTable("quotes", CommercialSchema.Name);
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.PricingMode).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Notes).HasMaxLength(2000);
        b.Property(e => e.TotalCost).HasPrecision(18, 2);
        b.Property(e => e.TotalPrice).HasPrecision(18, 2);
        b.Property(e => e.MarginPct).HasPrecision(9, 4);
        // Número com revisão (ADR 0016 adendo M7).
        b.HasIndex(e => new { e.Number, e.Revision }).IsUnique();
        b.HasIndex(e => e.Number).IsUnique().HasFilter("is_current").HasDatabaseName("ux_quotes_number_current");
        b.HasIndex(e => e.PartyId);
    }
}

internal sealed class QuoteItemConfiguration : IEntityTypeConfiguration<QuoteItem>
{
    public void Configure(EntityTypeBuilder<QuoteItem> b)
    {
        b.ToTable("quote_items", CommercialSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(12);
        b.Property(e => e.Description).HasMaxLength(300);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.UnitCost).HasPrecision(18, 6);
        b.Property(e => e.Pct).HasPrecision(7, 4);
        b.Property(e => e.UnitPrice).HasPrecision(18, 2);
        b.HasOne<Quote>().WithMany().HasForeignKey(e => e.QuoteId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.QuoteId);
        b.HasIndex(e => e.ItemId);
    }
}

internal sealed class QuoteItemCostConfiguration : IEntityTypeConfiguration<QuoteItemCost>
{
    public void Configure(EntityTypeBuilder<QuoteItemCost> b)
    {
        b.ToTable("quote_item_costs", CommercialSchema.Name);
        b.Property(e => e.CostType).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Description).HasMaxLength(200);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.UnitCost).HasPrecision(18, 6);
        b.Property(e => e.Total).HasPrecision(18, 2);
        b.HasOne<QuoteItem>().WithMany().HasForeignKey(e => e.QuoteItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.QuoteItemId);
        b.HasIndex(e => e.ItemId);
        b.HasIndex(e => e.PartyId);
        b.HasIndex(e => e.WorkCenterId);
    }
}

internal sealed class CommercialParameterConfiguration : IEntityTypeConfiguration<CommercialParameter>
{
    public void Configure(EntityTypeBuilder<CommercialParameter> b)
    {
        // Nomes próprios: finance.parameters já usa pk_parameters e ix_parameters_key.
        b.ToTable("parameters", CommercialSchema.Name);
        b.HasKey(e => e.Id).HasName("pk_commercial_parameters");
        b.Property(e => e.Key).HasMaxLength(64);
        b.HasIndex(e => e.Key).IsUnique().HasDatabaseName("ix_commercial_parameters_key");
        b.Property(e => e.Value).HasPrecision(7, 4);
    }
}
