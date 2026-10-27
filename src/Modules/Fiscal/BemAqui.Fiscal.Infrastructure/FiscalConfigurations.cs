using BemAqui.Fiscal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Fiscal.Infrastructure;

internal static class FiscalSchema
{
    public const string Name = "fiscal";
}

/// <summary>Integração com o EmissorFiscal (FIS-01): uma linha por tenant (<c>slot</c> único); chave cifrada, nunca em claro.</summary>
internal sealed class FiscalIntegrationSettingsConfiguration : IEntityTypeConfiguration<FiscalIntegrationSettings>
{
    public void Configure(EntityTypeBuilder<FiscalIntegrationSettings> b)
    {
        b.ToTable("integration_settings", FiscalSchema.Name, t => t.HasCheckConstraint("ck_integration_settings_slot", "slot = 1"));
        b.HasIndex(e => e.Slot).IsUnique().HasDatabaseName("ux_integration_settings_slot");
        b.Property(e => e.BaseUrl).HasMaxLength(FiscalIntegrationSettings.BaseUrlMaxLength);
        b.Property(e => e.ApiKeyCipher).HasMaxLength(4000);
        b.Property(e => e.IssuerDocument).HasMaxLength(14);
        b.Property(e => e.IssuerName).HasMaxLength(200);
        b.Property(e => e.Environment).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Scopes).HasMaxLength(500);
        b.Property(e => e.LastTestResult).HasMaxLength(40);
        b.Property(e => e.LastTestMessage).HasMaxLength(FiscalIntegrationSettings.MessageMaxLength);
        b.Ignore(e => e.ScopeList);
    }
}

/// <summary>
/// NF-e de saída (FIS-01, enxuta; a FIS-03a acrescenta itens, destinatário e vínculos). Chave de acesso única quando existe; o
/// documento do Emissor também. Índice da conferência de pendentes por situação e início.
/// </summary>
internal sealed class OutgoingInvoiceConfiguration : IEntityTypeConfiguration<OutgoingInvoice>
{
    public void Configure(EntityTypeBuilder<OutgoingInvoice> b)
    {
        b.ToTable("outgoing_invoices", FiscalSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_outgoing_invoices_attempt", "attempt >= 1");
            t.HasCheckConstraint("ck_outgoing_invoices_payload_hash", "payload_hash IS NULL OR length(payload_hash) = 64");
        });
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.PayloadHash).HasMaxLength(64);
        b.Property(e => e.TotalAmount).HasPrecision(18, 2);
        b.Property(e => e.AccessKey).HasMaxLength(44);
        b.Property(e => e.Protocol).HasMaxLength(30);
        b.Property(e => e.RejectionReason).HasMaxLength(OutgoingInvoice.ReasonMaxLength);
        b.Property(e => e.SuggestedAction).HasMaxLength(OutgoingInvoice.ReasonMaxLength);
        b.Property(e => e.XmlSha256).HasMaxLength(64);
        b.HasIndex(e => e.AccessKey).IsUnique().HasFilter("access_key IS NOT NULL").HasDatabaseName("ux_outgoing_invoices_access_key");
        b.HasIndex(e => e.EmitterDocumentId).IsUnique().HasFilter("emitter_document_id IS NOT NULL").HasDatabaseName("ux_outgoing_invoices_emitter_document_id");
        b.HasIndex(e => new { e.Status, e.ProcessingSince }).HasDatabaseName("ix_outgoing_invoices_status_processing_since");
        b.Ignore(e => e.IdempotencyKey);
        b.Ignore(e => e.IsPending);
        b.Ignore(e => e.IsFinal);
    }
}
