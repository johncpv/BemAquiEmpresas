using BemAqui.Purchasing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Purchasing.Infrastructure;

/// <summary>
/// Cotação (CMP-02): número único. Os <c>CHECK</c>s entram por SQL na migration <c>Rfqs</c>. Fornecedor, item e condição são de outros
/// módulos (só o id, sem FK, ADR 0006); as tabelas da cotação ligam-se entre si e à linha de SC por FK (mesmo módulo).
/// </summary>
internal sealed class RfqConfiguration : IEntityTypeConfiguration<Rfq>
{
    public void Configure(EntityTypeBuilder<Rfq> b)
    {
        b.ToTable("rfqs", PurchasingSchema.Name);
        b.Property(e => e.Number).HasMaxLength(30);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(e => e.Notes).HasMaxLength(RfqRules.MaxNotes);
        b.Property(e => e.CancelReason).HasMaxLength(RfqRules.MaxReason);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName("ux_rfqs_number");
        b.HasIndex(e => new { e.Status, e.CreatedAt }).HasDatabaseName("ix_rfqs_status_created_at");
    }
}

/// <summary>Fornecedor da cotação: um por terceiro e cotação (CA 2: o mesmo fornecedor não se repete).</summary>
internal sealed class RfqSupplierConfiguration : IEntityTypeConfiguration<RfqSupplier>
{
    public void Configure(EntityTypeBuilder<RfqSupplier> b)
    {
        b.ToTable("rfq_suppliers", PurchasingSchema.Name);
        b.Property(e => e.Freight).HasPrecision(18, 2);
        b.Property(e => e.IpiPct).HasPrecision(7, 4);
        b.Property(e => e.Notes).HasMaxLength(RfqRules.MaxNotes);
        b.Property(e => e.OrderSubmitError).HasMaxLength(RfqRules.MaxSubmitError);
        b.HasOne<Rfq>().WithMany().HasForeignKey(e => e.RfqId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.RfqId, e.PartyId }).IsUnique().HasDatabaseName("ux_rfq_suppliers_rfq_party");
        b.HasIndex(e => e.PartyId).HasDatabaseName("ix_rfq_suppliers_party_id");
    }
}

/// <summary>Linha da cotação: uma por item e cotação; o vencedor aponta para o fornecedor da própria cotação.</summary>
internal sealed class RfqLineConfiguration : IEntityTypeConfiguration<RfqLine>
{
    public void Configure(EntityTypeBuilder<RfqLine> b)
    {
        b.ToTable("rfq_lines", PurchasingSchema.Name);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.SelectionReason).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.SelectionNote).HasMaxLength(RfqRules.MaxSelectionNote);
        b.HasOne<Rfq>().WithMany().HasForeignKey(e => e.RfqId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RfqSupplier>().WithMany().HasForeignKey(e => e.SelectedSupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.RfqId, e.Sequence }).IsUnique().HasDatabaseName("ux_rfq_lines_rfq_sequence");
        b.HasIndex(e => new { e.RfqId, e.ItemId }).IsUnique().HasDatabaseName("ux_rfq_lines_rfq_item");
    }
}

/// <summary>Vínculo com as linhas de SC: cada linha de SC entra uma vez em cada linha da cotação.</summary>
internal sealed class RfqLineRequestConfiguration : IEntityTypeConfiguration<RfqLineRequest>
{
    public void Configure(EntityTypeBuilder<RfqLineRequest> b)
    {
        b.ToTable("rfq_line_requests", PurchasingSchema.Name);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.HasOne<Rfq>().WithMany().HasForeignKey(e => e.RfqId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RfqLine>().WithMany().HasForeignKey(e => e.RfqLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PurchaseRequestLine>().WithMany().HasForeignKey(e => e.RequestLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.RfqLineId, e.RequestLineId }).IsUnique().HasDatabaseName("ux_rfq_line_requests_line_request");
        b.HasIndex(e => e.RequestLineId).HasDatabaseName("ix_rfq_line_requests_request_line_id");
    }
}

/// <summary>Preço do fornecedor na linha: um por linha e fornecedor.</summary>
internal sealed class RfqQuoteConfiguration : IEntityTypeConfiguration<RfqQuote>
{
    public void Configure(EntityTypeBuilder<RfqQuote> b)
    {
        b.ToTable("rfq_quotes", PurchasingSchema.Name);
        b.Property(e => e.UnitPrice).HasPrecision(18, 6);
        b.Property(e => e.SupplierUnitPrice).HasPrecision(18, 6);
        b.Property(e => e.SupplierUnit).HasMaxLength(20);
        b.Property(e => e.AvailableQuantity).HasPrecision(18, 4);
        b.HasOne<Rfq>().WithMany().HasForeignKey(e => e.RfqId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RfqLine>().WithMany().HasForeignKey(e => e.RfqLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<RfqSupplier>().WithMany().HasForeignKey(e => e.RfqSupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.RfqLineId, e.RfqSupplierId }).IsUnique().HasDatabaseName("ux_rfq_quotes_line_supplier");
        b.HasIndex(e => e.RfqSupplierId).HasDatabaseName("ix_rfq_quotes_rfq_supplier_id");
        b.HasIndex(e => e.RfqId).HasDatabaseName("ix_rfq_quotes_rfq_id");
    }
}
