using BemAqui.Commercial.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Commercial.Infrastructure;

internal sealed class QuoteDispatchConfiguration : IEntityTypeConfiguration<QuoteDispatch>
{
    public void Configure(EntityTypeBuilder<QuoteDispatch> b)
    {
        b.ToTable("quote_dispatches", CommercialSchema.Name);
        b.Property(e => e.To).HasMaxLength(254);
        b.Property(e => e.Cc).HasMaxLength(800);
        b.HasOne<Quote>().WithMany().HasForeignKey(e => e.QuoteId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.QuoteId);
        b.HasIndex(e => e.CreatedAt);
    }
}
