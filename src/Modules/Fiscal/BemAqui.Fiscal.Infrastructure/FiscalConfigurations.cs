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

/// <summary>
/// Configuração fiscal (FIS-02a): uma linha por tenant (<c>slot</c> único); alíquotas em <c>numeric(7,4)</c>, CNAEs e tributos do custo em
/// <c>text[]</c>; do certificado A1 só os metadados (D12).
/// </summary>
internal sealed class FiscalSettingsConfiguration : IEntityTypeConfiguration<FiscalSettings>
{
    public void Configure(EntityTypeBuilder<FiscalSettings> b)
    {
        b.ToTable("fiscal_settings", FiscalSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_fiscal_settings_slot", "slot = 1");
            t.HasCheckConstraint("ck_fiscal_settings_nfe_series", "nfe_series IS NULL OR nfe_series BETWEEN 1 AND 999");
            t.HasCheckConstraint("ck_fiscal_settings_cancellation_window", "cancellation_window_hours BETWEEN 1 AND 168");
        });
        b.HasIndex(e => e.Slot).IsUnique().HasDatabaseName("ux_fiscal_settings_slot");
        b.Property(e => e.Regime).HasConversion<string>().HasMaxLength(32);
        b.Property(e => e.Environment).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.MixedSale).HasConversion<string>().HasMaxLength(40);
        b.Property(e => e.NfseIssLocation).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.CnaeMain).HasMaxLength(7);
        foreach (var cfop in new[] { nameof(FiscalSettings.CfopSaleInStateContributor), nameof(FiscalSettings.CfopSaleInStateFinal), nameof(FiscalSettings.CfopSaleInterstateContributor),
                     nameof(FiscalSettings.CfopSaleInterstateFinal), nameof(FiscalSettings.CfopResaleInState), nameof(FiscalSettings.CfopResaleInterstate) })
        {
            b.Property<string>(cfop).HasMaxLength(4);
        }

        b.Property(e => e.CstIcms).HasMaxLength(3);
        b.Property(e => e.CstPisCofins).HasMaxLength(2);
        b.Property(e => e.CstIpi).HasMaxLength(2);
        foreach (var rate in new[] { nameof(FiscalSettings.IcmsInternalRate), nameof(FiscalSettings.IcmsInterstateSouthSoutheastRate), nameof(FiscalSettings.IcmsInterstateOtherRate),
                     nameof(FiscalSettings.IcmsImportedRate), nameof(FiscalSettings.PisRate), nameof(FiscalSettings.CofinsRate), nameof(FiscalSettings.NfseIssRate),
                     nameof(FiscalSettings.CbsRate), nameof(FiscalSettings.IbsRate) })
        {
            b.Property<decimal>(rate).HasPrecision(7, 4);
        }

        b.Property(e => e.NfseServiceItem).HasMaxLength(5);
        b.Property(e => e.NfseMunicipalCode).HasMaxLength(8);
        b.Property(e => e.NfseCtn).HasMaxLength(20);
        b.Property(e => e.NfseNbs).HasMaxLength(20);
        b.Property(e => e.IbsCbsCst).HasMaxLength(3);
        b.Property(e => e.IbsCbsClassification).HasMaxLength(6);
        b.Property(e => e.VolumeSpecies).HasMaxLength(60);
        b.Property(e => e.CertificateSubject).HasMaxLength(300);
        b.Property(e => e.CertificateDocument).HasMaxLength(14);
        b.Property(e => e.CertificateThumbprint).HasMaxLength(64);
    }
}

/// <summary>Conferência por chave (FIS-02a CA 5): única por <c>setting_key</c>, com o hash do valor conferido.</summary>
internal sealed class SettingConfirmationConfiguration : IEntityTypeConfiguration<SettingConfirmation>
{
    public void Configure(EntityTypeBuilder<SettingConfirmation> b)
    {
        b.ToTable("setting_confirmations", FiscalSchema.Name, t => t.HasCheckConstraint("ck_setting_confirmations_hash", "length(value_hash) = 64"));
        b.Property(e => e.SettingKey).HasMaxLength(60);
        b.Property(e => e.ValueHash).HasMaxLength(64);
        b.HasIndex(e => e.SettingKey).IsUnique().HasDatabaseName("ux_setting_confirmations_key");
    }
}

/// <summary>Histórico das trocas de certificado (FIS-02a CA 13): só metadados.</summary>
internal sealed class CertificateUploadConfiguration : IEntityTypeConfiguration<CertificateUpload>
{
    public void Configure(EntityTypeBuilder<CertificateUpload> b)
    {
        b.ToTable("certificate_uploads", FiscalSchema.Name);
        b.Property(e => e.Thumbprint).HasMaxLength(64);
        b.Property(e => e.Subject).HasMaxLength(300);
        b.Property(e => e.Document).HasMaxLength(14);
        b.Property(e => e.Source).HasMaxLength(16);
        b.HasIndex(e => e.At).HasDatabaseName("ix_certificate_uploads_at");
    }
}
