using System.Text.Json;
using BemAqui.Assets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Assets.Infrastructure;

internal static class AssetsSchema
{
    public const string Name = "assets";
}

/// <summary>
/// <c>assets.assets</c> (PAT-01a): etiqueta única sem filtro (nunca reaproveitada, nem a do bem excluído); dinheiro em
/// <c>numeric(18,2)</c>, data de aquisição em <c>date</c>; índices dos filtros da lista (CA 9 e 11).
/// </summary>
internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> b)
    {
        b.ToTable("assets", AssetsSchema.Name);
        b.Property(e => e.Tag).HasMaxLength(20);
        b.Property(e => e.Name).HasMaxLength(AssetRules.NameMax);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Brand).HasMaxLength(AssetRules.TextMax);
        b.Property(e => e.Model).HasMaxLength(AssetRules.TextMax);
        b.Property(e => e.SerialNumber).HasMaxLength(AssetRules.TextMax);
        b.Property(e => e.AcquisitionValue).HasPrecision(18, 2);
        b.Property(e => e.InvoiceRef).HasMaxLength(AssetRules.InvoiceRefMax);
        b.Property(e => e.Notes).HasMaxLength(AssetRules.NotesMax);
        b.HasOne<AssetSite>().WithMany().HasForeignKey(e => e.SiteId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Tag).IsUnique().HasDatabaseName("ix_assets_tag");
        b.HasIndex(e => e.SerialNumber).HasFilter("serial_number IS NOT NULL AND NOT is_deleted").HasDatabaseName("ix_assets_serial_number");
        b.HasIndex(e => e.WorkCenterId).HasFilter("NOT is_deleted").HasDatabaseName("ix_assets_work_center_id");
        b.HasIndex(e => e.ResponsibleEmployeeId).HasFilter("NOT is_deleted").HasDatabaseName("ix_assets_responsible_employee_id");
        b.HasIndex(e => e.SupplierPartyId).HasFilter("NOT is_deleted").HasDatabaseName("ix_assets_supplier_party_id");
        b.HasIndex(e => new { e.Status, e.Type }).HasFilter("NOT is_deleted").HasDatabaseName("ix_assets_status_type");
    }
}

/// <summary><c>assets.parameters</c> (PAT-01b): chave única, valor em texto (<c>50x25</c>).</summary>
internal sealed class AssetParameterConfiguration : IEntityTypeConfiguration<AssetParameter>
{
    public void Configure(EntityTypeBuilder<AssetParameter> b)
    {
        b.ToTable("parameters", AssetsSchema.Name);
        b.HasKey(e => e.Id).HasName("pk_assets_parameters");
        b.Property(e => e.Key).HasMaxLength(64);
        b.Property(e => e.Value).HasMaxLength(100);
        b.HasIndex(e => e.Key).IsUnique().HasDatabaseName("ix_assets_parameters_key");
    }
}

/// <summary><c>assets.sites</c> (ADR 0038 §2): nome único entre os não excluídos e <c>seed_key</c> única.</summary>
internal sealed class AssetSiteConfiguration : IEntityTypeConfiguration<AssetSite>
{
    public void Configure(EntityTypeBuilder<AssetSite> b)
    {
        b.ToTable("sites", AssetsSchema.Name);
        b.Property(e => e.Name).HasMaxLength(100);
        b.Property(e => e.SeedKey).HasMaxLength(50);
        b.HasIndex(e => e.Name).IsUnique().HasFilter("NOT is_deleted").HasDatabaseName("ix_sites_name");
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL").HasDatabaseName("ix_sites_seed_key");
    }
}

/// <summary>
/// <c>assets.asset_movements</c> (PAT-02): movimento imutável do bem, com antes e depois; data de negócio em <c>date</c>, valor da venda em
/// <c>numeric(18,2)</c>; índice pelo bem e data para a aba Movimentações (do mais recente ao mais antigo).
/// </summary>
internal sealed class AssetMovementConfiguration : IEntityTypeConfiguration<AssetMovement>
{
    public void Configure(EntityTypeBuilder<AssetMovement> b)
    {
        b.ToTable("asset_movements", AssetsSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_asset_movements_disposal_value", "disposal_value IS NULL OR disposal_value >= 0");
            t.HasCheckConstraint("ck_asset_movements_kind", "kind IN ('Transfer', 'Disposal', 'Reactivation')");
        });
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.DisposalReason).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Reason).HasMaxLength(AssetMovementRules.ReasonMax);
        b.Property(e => e.DisposalValue).HasPrecision(18, 2);
        b.HasOne<Asset>().WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.AssetId, e.MovedOn, e.CreatedAt }).HasDatabaseName("ix_asset_movements_asset_id_moved_on");
        b.HasIndex(e => e.FromEmployeeId).HasFilter("from_employee_id IS NOT NULL").HasDatabaseName("ix_asset_movements_from_employee_id");
        b.HasIndex(e => e.ToEmployeeId).HasFilter("to_employee_id IS NOT NULL").HasDatabaseName("ix_asset_movements_to_employee_id");
        b.HasIndex(e => e.BuyerPartyId).HasFilter("buyer_party_id IS NOT NULL").HasDatabaseName("ix_asset_movements_buyer_party_id");
    }
}

/// <summary>
/// <c>assets.maintenance_plans</c> (PAT-03a): checklist em jsonb (lista de textos), datas de negócio em <c>date</c>, horímetro em
/// <c>numeric(18,4)</c>, <c>xmin</c>; índice pelo bem e o do próximo vencimento dos ativos para o job diário e a lista.
/// </summary>
internal sealed class MaintenancePlanConfiguration : IEntityTypeConfiguration<MaintenancePlan>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<MaintenancePlan> b)
    {
        b.ToTable("maintenance_plans", AssetsSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_maintenance_plans_frequency_value", "frequency_value > 0");
            t.HasCheckConstraint("ck_maintenance_plans_lead_days", "lead_days >= 0");
        });
        b.Property(e => e.Description).HasMaxLength(MaintenanceRules.DescriptionMax);
        b.Property(e => e.FrequencyType).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.LastDoneMeter).HasPrecision(18, 4);
        b.Property(e => e.NextDueMeter).HasPrecision(18, 4);
        b.Property(e => e.Checklist).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, Json),
            s => JsonSerializer.Deserialize<List<string>>(s, Json) ?? new List<string>(),
            new ValueComparer<List<string>>((x, y) => x!.SequenceEqual(y!), v => v.Aggregate(0, (h, i) => HashCode.Combine(h, i.GetHashCode(StringComparison.Ordinal))), v => v.ToList()));
        b.HasOne<Asset>().WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.AssetId).HasDatabaseName("ix_maintenance_plans_asset_id");
        b.HasIndex(e => e.NextDueAt).HasFilter("is_active").HasDatabaseName("ix_maintenance_plans_next_due_at");
        b.HasIndex(e => e.DefaultSupplierPartyId).HasFilter("default_supplier_party_id IS NOT NULL").HasDatabaseName("ix_maintenance_plans_default_supplier_party_id");
    }
}

/// <summary><c>assets.asset_meter_readings</c> (PAT-03a CA 2): leitura digitada, só inclusão; índice pelo bem e data.</summary>
internal sealed class AssetMeterReadingConfiguration : IEntityTypeConfiguration<AssetMeterReading>
{
    public void Configure(EntityTypeBuilder<AssetMeterReading> b)
    {
        b.ToTable("asset_meter_readings", AssetsSchema.Name, t => t.HasCheckConstraint("ck_asset_meter_readings_value", "value >= 0"));
        b.Property(e => e.Value).HasPrecision(18, 4);
        b.HasOne<Asset>().WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.AssetId, e.ReadOn, e.Value }).HasDatabaseName("ix_asset_meter_readings_asset_id_read_on");
    }
}

/// <summary>
/// <c>assets.maintenance_orders</c> (PAT-03a): número <c>MAN</c> único, uma OS por vencimento do plano (índice único parcial
/// <c>(plan_id, due_key)</c>, CA 3), <c>xmin</c> (CA 8); índices pelo bem, pela situação e pelo prestador. Custo e título a pagar entram na PAT-03b.
/// </summary>
internal sealed class MaintenanceOrderConfiguration : IEntityTypeConfiguration<MaintenanceOrder>
{
    public void Configure(EntityTypeBuilder<MaintenanceOrder> b)
    {
        b.ToTable("maintenance_orders", AssetsSchema.Name, t =>
            t.HasCheckConstraint("ck_maintenance_orders_status", "status IN ('Open', 'InProgress', 'Done', 'Cancelled')"));
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.DueKey).HasMaxLength(40);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Problem).HasMaxLength(MaintenanceRules.ProblemMax);
        b.Property(e => e.WorkDone).HasMaxLength(MaintenanceRules.WorkDoneMax);
        b.Property(e => e.CancelReason).HasMaxLength(MaintenanceRules.ReasonMax);
        b.HasOne<Asset>().WithMany().HasForeignKey(e => e.AssetId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MaintenancePlan>().WithMany().HasForeignKey(e => e.PlanId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName("ix_maintenance_orders_number");
        b.HasIndex(e => new { e.PlanId, e.DueKey }).IsUnique().HasFilter("due_key IS NOT NULL").HasDatabaseName(BemAqui.Assets.Application.MaintenanceService.DueIndex);
        b.HasIndex(e => new { e.AssetId, e.CreatedAt }).HasDatabaseName("ix_maintenance_orders_asset_id_created_at");
        b.HasIndex(e => e.Status).HasDatabaseName("ix_maintenance_orders_status");
        b.HasIndex(e => e.SupplierPartyId).HasFilter("supplier_party_id IS NOT NULL").HasDatabaseName("ix_maintenance_orders_supplier_party_id");
    }
}

/// <summary><c>assets.maintenance_order_checklist</c> (PAT-03a CA 6): linha a linha, com o resultado; ordem única na OS.</summary>
internal sealed class MaintenanceOrderChecklistConfiguration : IEntityTypeConfiguration<MaintenanceOrderChecklistItem>
{
    public void Configure(EntityTypeBuilder<MaintenanceOrderChecklistItem> b)
    {
        b.ToTable("maintenance_order_checklist", AssetsSchema.Name);
        b.Property(e => e.Text).HasMaxLength(MaintenanceRules.ChecklistItemMax);
        b.Property(e => e.Result).HasConversion<string>().HasMaxLength(16);
        b.HasOne<MaintenanceOrder>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.OrderId, e.Sequence }).IsUnique().HasDatabaseName("ix_maintenance_order_checklist_order_id_sequence");
    }
}
