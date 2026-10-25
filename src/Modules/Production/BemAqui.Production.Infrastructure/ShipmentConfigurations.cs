using BemAqui.Production.Application;
using BemAqui.Production.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Production.Infrastructure;

/// <summary>
/// Romaneio (PRD-06b): número único (D15), situação em texto, peso e quantidades <c>numeric(18,4)</c>; projeto, cliente e transportadora
/// são de outros módulos (só o id, sem FK, ADR 0006). As listas filtram por projeto, situação e quem montou.
/// </summary>
internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> b)
    {
        b.ToTable("shipments", ProductionSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_shipments_volumes", "volumes IS NULL OR volumes > 0");
            t.HasCheckConstraint("ck_shipments_gross_weight", "gross_weight IS NULL OR gross_weight >= 0");
        });
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.DeliveryAddress).HasMaxLength(ShipmentRules.MaxAddress);
        b.Property(e => e.VehiclePlate).HasMaxLength(ShipmentRules.MaxPlate);
        b.Property(e => e.DriverName).HasMaxLength(ShipmentRules.MaxDriver);
        b.Property(e => e.GrossWeight).HasPrecision(18, 4);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.CancelReason).HasMaxLength(ShipmentRules.MaxReason);
        b.Property(e => e.ReversalReason).HasMaxLength(ShipmentRules.MaxReason);
        b.HasIndex(e => e.Number).IsUnique();
        b.HasIndex(e => new { e.ProjectId, e.Status });
        b.HasIndex(e => e.Status);
        b.HasIndex(e => new { e.PartyId, e.CreatedAt });
    }
}

/// <summary>Linha do romaneio: uma por sequência no romaneio; a saída no estoque é pela chave (Shipment, id, sequência).</summary>
internal sealed class ShipmentLineConfiguration : IEntityTypeConfiguration<ShipmentLine>
{
    public void Configure(EntityTypeBuilder<ShipmentLine> b)
    {
        b.ToTable("shipment_lines", ProductionSchema.Name, t => t.HasCheckConstraint("ck_shipment_lines_quantity", "quantity > 0"));
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.LotNumber).HasMaxLength(60);
        b.HasOne<Shipment>().WithMany().HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ShipmentId, e.Sequence }).IsUnique();
        b.HasIndex(e => e.ItemId);
    }
}

/// <summary>
/// Série da linha (CA 2 e 16): índice único parcial <c>ux_shipment_line_serials_active</c> de <c>serial_id</c> enquanto o romaneio não foi
/// cancelado nem estornado (<c>released_at</c> nulo): duas montagens com a mesma série, só uma grava.
/// </summary>
internal sealed class ShipmentLineSerialConfiguration : IEntityTypeConfiguration<ShipmentLineSerial>
{
    public const string ActiveIndex = "ux_shipment_line_serials_active";

    public void Configure(EntityTypeBuilder<ShipmentLineSerial> b)
    {
        b.ToTable("shipment_line_serials", ProductionSchema.Name);
        b.Property(e => e.SerialNumber).HasMaxLength(60);
        b.HasOne<Shipment>().WithMany().HasForeignKey(e => e.ShipmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ShipmentLine>().WithMany().HasForeignKey(e => e.LineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.SerialId).IsUnique().HasFilter("released_at IS NULL").HasDatabaseName(ActiveIndex);
        b.HasIndex(e => e.ShipmentId);
        b.HasIndex(e => e.LineId);
    }
}
