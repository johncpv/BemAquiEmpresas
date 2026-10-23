using BemAqui.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Purchasing.Infrastructure;

internal static class PurchasingSchema
{
    public const string Name = "purchasing";
}

/// <summary>
/// Solicitação de compra (CMP-01): número único; projeto e OS são de outros módulos (só o id, sem FK, ADR 0006). Os <c>CHECK</c>s entram
/// por SQL na migration <c>PurchasingRequests</c>.
/// </summary>
internal sealed class PurchaseRequestConfiguration : IEntityTypeConfiguration<PurchaseRequest>
{
    public void Configure(EntityTypeBuilder<PurchaseRequest> b)
    {
        b.ToTable("purchase_requests", PurchasingSchema.Name);
        b.Property(e => e.Number).HasMaxLength(30);
        b.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Notes).HasMaxLength(PurchaseRequestRules.MaxNotes);
        b.Property(e => e.CancelReason).HasMaxLength(PurchaseRequestRules.MaxReason);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName("ux_purchase_requests_number");
        b.HasIndex(e => new { e.Status, e.Origin }).HasDatabaseName("ix_purchase_requests_status_origin");
        b.HasIndex(e => e.ProductionOrderId).HasFilter("production_order_id IS NOT NULL").HasDatabaseName("ix_purchase_requests_production_order_id");
        b.HasIndex(e => e.ProjectId).HasFilter("project_id IS NOT NULL").HasDatabaseName("ix_purchase_requests_project_id");
    }
}

/// <summary>
/// Linha da solicitação: uma linha da falta em aberto por OS e item e uma do mínimo em aberto por item (índices únicos parciais: a
/// recomposição e o job nunca duplicam, CA 5 e 10). O item é do Inventory (só o id).
/// </summary>
internal sealed class PurchaseRequestLineConfiguration : IEntityTypeConfiguration<PurchaseRequestLine>
{
    public const string OpenShortageIndex = "ux_purchase_request_lines_open_shortage";
    public const string OpenMinStockIndex = "ux_purchase_request_lines_open_min_stock";

    public void Configure(EntityTypeBuilder<PurchaseRequestLine> b)
    {
        b.ToTable("purchase_request_lines", PurchasingSchema.Name);
        b.Property(e => e.Origin).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.EnteredQuantity).HasPrecision(18, 4);
        b.Property(e => e.OrderedQuantity).HasPrecision(18, 4);
        b.Property(e => e.CancelReason).HasMaxLength(PurchaseRequestRules.MaxReason);
        b.HasOne<PurchaseRequest>().WithMany().HasForeignKey(e => e.RequestId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.RequestId, e.Sequence }).IsUnique().HasDatabaseName("ux_purchase_request_lines_request_sequence");
        b.HasIndex(e => new { e.ProductionOrderId, e.ItemId }, OpenShortageIndex).IsUnique()
            .HasFilter("status = 'Open' AND origin = 'BomShortage' AND NOT manually_edited").HasDatabaseName(OpenShortageIndex);
        b.HasIndex(e => e.ItemId, OpenMinStockIndex).IsUnique().HasFilter("status = 'Open' AND origin = 'MinStock' AND NOT manually_edited").HasDatabaseName(OpenMinStockIndex);
        b.HasIndex(e => new { e.ItemId, e.Status }).HasDatabaseName("ix_purchase_request_lines_item_status");
        b.HasIndex(e => e.ProductionOrderId).HasFilter("production_order_id IS NOT NULL").HasDatabaseName("ix_purchase_request_lines_production_order_id");
    }
}

/// <summary>Parâmetros de Compras (ADR 0016 adendo M6): chave única, como os da Produção.</summary>
internal sealed class PurchasingParameterConfiguration : IEntityTypeConfiguration<PurchasingParameter>
{
    public void Configure(EntityTypeBuilder<PurchasingParameter> b)
    {
        b.ToTable("parameters", PurchasingSchema.Name);
        b.HasKey(e => e.Id).HasName("pk_purchasing_parameters");
        b.Property(e => e.Key).HasMaxLength(64);
        b.HasIndex(e => e.Key).IsUnique().HasDatabaseName("ix_purchasing_parameters_key");
        b.Property(e => e.Value).HasPrecision(9, 2);
    }
}
