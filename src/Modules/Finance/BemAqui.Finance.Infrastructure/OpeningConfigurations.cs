using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

internal sealed class OpeningSetupConfiguration : IEntityTypeConfiguration<OpeningSetup>
{
    public void Configure(EntityTypeBuilder<OpeningSetup> b)
    {
        b.ToTable("opening_setup", FinanceSchema.Name);
        b.Property(e => e.Notes).HasMaxLength(500);
    }
}

internal sealed class OpeningBatchConfiguration : IEntityTypeConfiguration<OpeningBatch>
{
    public void Configure(EntityTypeBuilder<OpeningBatch> b)
    {
        b.ToTable("opening_batches", FinanceSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Total).HasPrecision(18, 2);
    }
}
