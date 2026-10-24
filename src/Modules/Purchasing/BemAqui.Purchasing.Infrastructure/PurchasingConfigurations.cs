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
        b.Property(e => e.Text).HasMaxLength(64);
    }
}

/// <summary>
/// Pedido de compra (CMP-03a): número único; fornecedor, projeto, condição, conta e cotação são de outros módulos (só o id, sem FK,
/// ADR 0006). Os <c>CHECK</c>s entram por SQL na migration <c>PurchaseOrders</c>.
/// </summary>
internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.ToTable("purchase_orders", PurchasingSchema.Name);
        b.Property(e => e.Number).HasMaxLength(30);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        foreach (var money in new[] { nameof(PurchaseOrder.Subtotal), nameof(PurchaseOrder.Freight), nameof(PurchaseOrder.IpiAmount), nameof(PurchaseOrder.Total), nameof(PurchaseOrder.ReferenceAmount) })
        {
            b.Property(money).HasPrecision(18, 2);
        }

        b.Property(e => e.IpiPct).HasPrecision(7, 4);
        b.Property(e => e.Notes).HasMaxLength(PurchaseOrderRules.MaxNotes);
        b.Property(e => e.RejectionComment).HasMaxLength(PurchaseOrderRules.MaxComment);
        b.Property(e => e.CancelReason).HasMaxLength(PurchaseOrderRules.MaxReason);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName("ux_purchase_orders_number");
        b.HasIndex(e => e.Status).HasDatabaseName("ix_purchase_orders_status");
        b.HasIndex(e => new { e.PartyId, e.SubmittedAt }).HasDatabaseName("ix_purchase_orders_party_submitted");
        b.HasIndex(e => e.ProjectId).HasFilter("project_id IS NOT NULL").HasDatabaseName("ix_purchase_orders_project_id");
        b.HasIndex(e => e.RfqId).HasFilter("rfq_id IS NOT NULL").HasDatabaseName("ix_purchase_orders_rfq_id");
    }
}

/// <summary>Linha do pedido: item do Inventory (só o id); as linhas de SC atendidas em <c>uuid[]</c>.</summary>
internal sealed class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.ToTable("purchase_order_lines", PurchasingSchema.Name);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.EnteredQuantity).HasPrecision(18, 4);
        b.Property(e => e.QuantityReceived).HasPrecision(18, 4);
        b.Property(e => e.UnitPrice).HasPrecision(18, 6);
        b.Property(e => e.LineTotal).HasPrecision(18, 2);
        b.Property(e => e.CostType).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.RequestLineIds).HasColumnType("uuid[]");
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.OrderId).HasDatabaseName("ix_purchase_order_lines_order_id");
        b.HasIndex(e => e.ItemId).HasDatabaseName("ix_purchase_order_lines_item_id");
    }
}

/// <summary>Níveis por tentativa (ADR 0024 §4): um por pedido, tentativa e nível.</summary>
internal sealed class PurchaseOrderLevelConfiguration : IEntityTypeConfiguration<PurchaseOrderLevel>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLevel> b)
    {
        b.ToTable("purchase_order_levels", PurchasingSchema.Name);
        b.Property(e => e.ApproverName).HasMaxLength(120);
        b.Property(e => e.ReferenceAmount).HasPrecision(18, 2);
        b.Property(e => e.SplitWarning).HasMaxLength(200);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(12);
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.OrderId, e.Attempt, e.Level }).IsUnique().HasDatabaseName("ux_purchase_order_levels_attempt_level");
    }
}

/// <summary>Linha do tempo das decisões (CA 23): imutável.</summary>
internal sealed class PurchaseOrderApprovalConfiguration : IEntityTypeConfiguration<PurchaseOrderApproval>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderApproval> b)
    {
        b.ToTable("purchase_order_approvals", PurchasingSchema.Name);
        b.Property(e => e.Decision).HasConversion<string>().HasMaxLength(12);
        b.Property(e => e.ReferenceAmount).HasPrecision(18, 2);
        b.Property(e => e.Comment).HasMaxLength(PurchaseOrderRules.MaxComment);
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.OrderId, e.Attempt }).HasDatabaseName("ix_purchase_order_approvals_order_attempt");
    }
}
