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
                     nameof(FiscalSettings.CbsRate), nameof(FiscalSettings.IbsRate), nameof(FiscalSettings.IbsCityRate) })
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

/// <summary>
/// Regras fiscais (FIS-02b): uma linha por versão (<c>rule_id</c> + <c>rule_version</c> únicos); no máximo um rascunho e uma ativa por regra
/// (índice parcial do rascunho; a ativa única pelo serviço); parâmetros de cada tributo e as condições em jsonb tipado (percentuais com até 4 casas, validados no domínio).
/// </summary>
internal sealed class TaxRuleConfiguration : IEntityTypeConfiguration<TaxRule>
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<TaxRule> b)
    {
        b.ToTable("tax_rules", FiscalSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_tax_rules_version", "rule_version >= 1");
            t.HasCheckConstraint("ck_tax_rules_validity", "valid_to IS NULL OR valid_to >= valid_from");
        });
        b.Property(e => e.Operation).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Name).HasMaxLength(TaxRule.NameMaxLength);
        b.Property(e => e.NatureOfOperation).HasMaxLength(TaxRule.NatureMaxLength);
        b.Property(e => e.CfopInternal).HasMaxLength(4);
        b.Property(e => e.CfopInterstate).HasMaxLength(4);
        b.Property(e => e.ComplementaryInfo).HasMaxLength(TaxRule.InfoMaxLength);
        b.Property(e => e.SeedKey).HasMaxLength(40);
        b.Property(e => e.DeactivationReason).HasMaxLength(TaxRule.ReasonMaxLength);
        Jsonb(b.Property(e => e.Icms));
        Jsonb(b.Property(e => e.IcmsSt));
        Jsonb(b.Property(e => e.Fcp));
        Jsonb(b.Property(e => e.Ipi));
        Jsonb(b.Property(e => e.Pis));
        Jsonb(b.Property(e => e.Cofins));
        Jsonb(b.Property(e => e.Iss));
        Jsonb(b.Property(e => e.IbsCbs));
        Jsonb(b.Property(e => e.Difal));
        b.Property(e => e.Conditions).HasColumnType("jsonb").HasConversion(
            v => System.Text.Json.JsonSerializer.Serialize(v, Json),
            s => System.Text.Json.JsonSerializer.Deserialize<TaxRuleConditions>(s, Json) ?? TaxRuleConditions.Any,
            new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<TaxRuleConditions>(
                (x, y) => System.Text.Json.JsonSerializer.Serialize(x, Json) == System.Text.Json.JsonSerializer.Serialize(y, Json),
                v => System.Text.Json.JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
                v => System.Text.Json.JsonSerializer.Deserialize<TaxRuleConditions>(System.Text.Json.JsonSerializer.Serialize(v, Json), Json)!));
        b.Ignore(e => e.IsConfirmed);
        b.HasIndex(e => new { e.RuleId, e.RuleVersion }).IsUnique().HasDatabaseName("ux_tax_rules_rule_version");
        // Um rascunho por regra; a ativa única é garantida no serviço (a troca de versão atualiza as duas linhas no mesmo SaveChanges).
        b.HasIndex(e => e.RuleId, "ux_tax_rules_one_draft").IsUnique().HasFilter("status = 'Draft'").HasDatabaseName("ux_tax_rules_one_draft");
        b.HasIndex(e => new { e.Status, e.Operation }).HasDatabaseName("ix_tax_rules_status_operation");
    }

    /// <summary>Parâmetro de um tributo em jsonb (null = o tributo não se aplica); records só com escalares, igualdade por valor.</summary>
    private static void Jsonb<T>(PropertyBuilder<T?> property)
        where T : class =>
        property.HasColumnType("jsonb").HasConversion(
            v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v, Json),
            s => s == null ? null : System.Text.Json.JsonSerializer.Deserialize<T>(s, Json));
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

/// <summary>Valor em jsonb com igualdade pelo JSON (records e listas de records, FIS-04a).</summary>
internal static class FiscalJsonb
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static PropertyBuilder<T> Jsonb<T>(this PropertyBuilder<T> property, T empty) =>
        property.HasColumnType("jsonb").HasConversion(
            v => System.Text.Json.JsonSerializer.Serialize(v, Json),
            s => System.Text.Json.JsonSerializer.Deserialize<T>(s, Json) ?? empty,
            new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<T>(
                (x, y) => System.Text.Json.JsonSerializer.Serialize(x, Json) == System.Text.Json.JsonSerializer.Serialize(y, Json),
                v => System.Text.Json.JsonSerializer.Serialize(v, Json).GetHashCode(StringComparison.Ordinal),
                v => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(v, Json), Json)!));
}

/// <summary>
/// NF-e de entrada (FIS-04a): chave única entre as não recusadas (índice parcial <c>ux_incoming_invoices_access_key</c>, CA 4); totais,
/// duplicatas, endereço do emitente e divergências em jsonb; lista por situação e importação.
/// </summary>
internal sealed class IncomingInvoiceConfiguration : IEntityTypeConfiguration<IncomingInvoice>
{
    public void Configure(EntityTypeBuilder<IncomingInvoice> b)
    {
        b.ToTable("incoming_invoices", FiscalSchema.Name, t => t.HasCheckConstraint("ck_incoming_invoices_access_key", "length(access_key) = 44"));
        b.Property(e => e.AccessKey).HasMaxLength(44);
        b.Property(e => e.IssuerDocument).HasMaxLength(14);
        b.Property(e => e.IssuerName).HasMaxLength(200);
        b.Property(e => e.IssuerTradeName).HasMaxLength(200);
        b.Property(e => e.IssuerStateRegistration).HasMaxLength(20);
        b.Property(e => e.IssuerAddress).HasColumnType("jsonb").HasConversion(
            v => v == null ? null : System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
            s => s == null ? null : System.Text.Json.JsonSerializer.Deserialize<IncomingIssuerAddress>(s, (System.Text.Json.JsonSerializerOptions?)null));
        b.Property(e => e.Source).HasConversion<string>().HasMaxLength(8);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Totals).Jsonb(new IncomingInvoiceTotals(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        b.Property(e => e.Total).HasPrecision(18, 2);
        b.Property(e => e.Duplicates).Jsonb<IReadOnlyList<IncomingDuplicate>>([]);
        b.Property(e => e.Divergences).Jsonb<IReadOnlyList<IncomingDivergence>>([]);
        b.Property(e => e.SignerDocument).HasMaxLength(14);
        b.Property(e => e.Protocol).HasMaxLength(20);
        b.Property(e => e.WithoutOrderReason).HasMaxLength(IncomingInvoice.ReasonMaxLength);
        b.Property(e => e.RejectedReason).HasMaxLength(IncomingInvoice.ReasonMaxLength);
        b.Property(e => e.InputTaxCreditApplied).HasMaxLength(IncomingInvoice.InputTaxCreditRuleMaxLength);
        b.Property(e => e.InputTaxCreditAmount).HasPrecision(18, 2);
        b.HasIndex(e => e.GoodsReceiptId).HasFilter("goods_receipt_id IS NOT NULL").HasDatabaseName("ix_incoming_invoices_goods_receipt_id");
        b.Ignore(e => e.IsEditable);
        b.HasIndex(e => e.AccessKey).IsUnique().HasFilter("status <> 'Rejected'").HasDatabaseName(IncomingInvoiceConfiguration.AccessKeyIndex);
        b.HasIndex(e => new { e.Status, e.ImportedAt }).HasDatabaseName("ix_incoming_invoices_status_imported_at");
        b.HasIndex(e => e.PartyId).HasDatabaseName("ix_incoming_invoices_party_id");
    }

    public const string AccessKeyIndex = "ux_incoming_invoices_access_key";
}

/// <summary>Itens da NF-e de entrada (FIS-04a): quantidades com 4 casas, preço da nota com até 10, preço em unidade de estoque com 6.</summary>
internal sealed class IncomingInvoiceItemConfiguration : IEntityTypeConfiguration<IncomingInvoiceItem>
{
    public void Configure(EntityTypeBuilder<IncomingInvoiceItem> b)
    {
        b.ToTable("incoming_invoice_items", FiscalSchema.Name);
        b.HasOne<IncomingInvoice>().WithMany().HasForeignKey(e => e.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.SupplierCode).HasMaxLength(60);
        b.Property(e => e.Description).HasMaxLength(120);
        b.Property(e => e.Ncm).HasMaxLength(8);
        b.Property(e => e.Cfop).HasMaxLength(4);
        b.Property(e => e.Unit).HasMaxLength(6);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.Property(e => e.UnitPrice).HasPrecision(24, 10);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.Discount).HasPrecision(18, 2);
        b.Property(e => e.Taxes).Jsonb(new IncomingItemTaxes(null, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        b.Property(e => e.LotNumber).HasMaxLength(20);
        b.Property(e => e.OrderNumber).HasMaxLength(15);
        b.Property(e => e.OrderItem).HasMaxLength(6);
        b.Property(e => e.ConversionFactor).HasPrecision(18, 6);
        b.Property(e => e.StockQuantity).HasPrecision(18, 4);
        b.Property(e => e.StockUnitPrice).HasPrecision(18, 6);
        b.Property(e => e.MappingSource).HasConversion<string>().HasMaxLength(16);
        b.Ignore(e => e.IsResolved);
        b.HasIndex(e => new { e.InvoiceId, e.Sequence }).IsUnique().HasDatabaseName("ux_incoming_invoice_items_invoice_sequence");
    }
}
