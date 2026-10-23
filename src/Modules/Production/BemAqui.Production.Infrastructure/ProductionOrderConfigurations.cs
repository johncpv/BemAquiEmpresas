using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Production.Infrastructure;

internal sealed class ProductionOrderConfiguration : IEntityTypeConfiguration<ProductionOrder>
{
    public void Configure(EntityTypeBuilder<ProductionOrder> b)
    {
        // PRD-01 CA 19: mesma tabela para os dois tipos, com CHECK por tipo. A versão anterior só lê linhas de instalação.
        b.ToTable("production_orders", ProductionSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_production_orders_installation",
                "type <> 'Installation' OR (installer_id IS NOT NULL AND contract_value IS NOT NULL AND planned_start IS NOT NULL AND planned_end IS NOT NULL)");
            t.HasCheckConstraint("ck_production_orders_fabrication",
                "type <> 'Fabrication' OR (installer_id IS NULL AND contract_value IS NULL AND project_item_id IS NOT NULL AND quantity IS NOT NULL AND quantity > 0)");
        });
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.ContractValue).HasPrecision(18, 2);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.AcceptedByName).HasMaxLength(120);
        b.Property(e => e.CancelReason).HasMaxLength(500);
        b.Property(e => e.CompletionReversalReason).HasMaxLength(500);
        b.HasIndex(e => e.Number).IsUnique();
        b.HasIndex(e => new { e.ProjectId, e.Type });
        b.HasIndex(e => e.InstallerId);
        b.HasIndex(e => new { e.Type, e.Status });
        b.HasIndex(e => e.ProductId);
        b.HasIndex(e => e.RoutingTemplateId);
        // D14: uma OS de fabricação não cancelada por item do projeto (idempotência do consumidor e do "Gerar OS").
        b.HasIndex(e => e.ProjectItemId).IsUnique().HasDatabaseName("ux_production_orders_fabrication_item")
            .HasFilter("type = 'Fabrication' AND status <> 'Cancelled'");
    }
}

internal sealed class ProductionOrderStepConfiguration : IEntityTypeConfiguration<ProductionOrderStep>
{
    public void Configure(EntityTypeBuilder<ProductionOrderStep> b)
    {
        b.ToTable("production_order_steps", ProductionSchema.Name);
        b.Property(e => e.Name).HasMaxLength(200);
        b.Property(e => e.Code).HasMaxLength(1);
        b.Property(e => e.StandardHours).HasPrecision(18, 4);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.CompletionReason).HasMaxLength(200);
        b.HasOne<ProductionOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.OrderId);
        b.HasIndex(e => e.WorkCenterId);
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
        b.HasOne<ProductionOrderStep>().WithMany().HasForeignKey(e => e.StepId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.OrderId);
        b.HasIndex(e => e.StepId);
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
