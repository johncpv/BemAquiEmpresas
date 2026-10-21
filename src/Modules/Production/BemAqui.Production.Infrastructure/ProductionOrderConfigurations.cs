using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Production.Infrastructure;

internal sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> b)
    {
        b.ToTable("production_orders", ProductionSchema.Name);
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.ContractValue).HasPrecision(18, 2);
        b.Property(e => e.AcceptedByName).HasMaxLength(120);
        b.Property(e => e.CancelReason).HasMaxLength(500);
        b.HasIndex(e => e.Number).IsUnique();
        b.HasIndex(e => new { e.ProjectId, e.Type });
        b.HasIndex(e => e.InstallerId);
    }
}

internal sealed class ProductionOrderStepConfiguration : IEntityTypeConfiguration<ProductionOrderStep>
{
    public void Configure(EntityTypeBuilder<ProductionOrderStep> b)
    {
        b.ToTable("production_order_steps", ProductionSchema.Name);
        b.Property(e => e.Name).HasMaxLength(200);
        b.HasOne<ProductionOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.OrderId);
    }
}

internal sealed class ProductionOrderChecklistConfiguration : IEntityTypeConfiguration<ProductionOrderChecklistItem>
{
    public void Configure(EntityTypeBuilder<ProductionOrderChecklistItem> b)
    {
        b.ToTable("production_order_checklist", ProductionSchema.Name);
        b.Property(e => e.Text).HasMaxLength(200);
        b.Property(e => e.Result).HasConversion<string>().HasMaxLength(16);
        b.HasOne<ProductionOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.OrderId);
    }
}

internal sealed class ProductionOrderProjectItemConfiguration : IEntityTypeConfiguration<ProductionOrderProjectItem>
{
    public void Configure(EntityTypeBuilder<ProductionOrderProjectItem> b)
    {
        b.ToTable("production_order_project_items", ProductionSchema.Name);
        b.HasOne<ProductionOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.OrderId, e.ProjectItemId }).IsUnique();
        b.HasIndex(e => e.ProjectItemId);
    }
}

internal sealed class InstallationTemplateLineConfiguration : IEntityTypeConfiguration<InstallationTemplateLine>
{
    public void Configure(EntityTypeBuilder<InstallationTemplateLine> b)
    {
        b.ToTable("installation_template", ProductionSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(12);
        b.Property(e => e.Text).HasMaxLength(200);
    }
}
