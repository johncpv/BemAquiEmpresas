using BemAqui.Pos.Application;
using BemAqui.Pos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Pos.Infrastructure;

/// <summary><c>pos.stores</c> (PDV-01 CA 2): código único; local de estoque e tabela de preço são só ids (sem FK para outro módulo).</summary>
internal sealed class StoreConfiguration : IEntityTypeConfiguration<Store>
{
    public void Configure(EntityTypeBuilder<Store> b)
    {
        b.ToTable("stores", PosSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_stores_offline_window", "offline_window_hours BETWEEN 1 AND 168");
            t.HasCheckConstraint("ck_stores_parameters", "cash_break_tolerance >= 0 AND exchange_days >= 0 AND cash_drop_alert_amount >= 0");
            t.HasCheckConstraint("ck_stores_cash_ceiling", "cash_ceiling_amount IS NULL OR cash_ceiling_amount > 0");
            t.HasCheckConstraint("ck_stores_discount_limit", "discount_limit_percent BETWEEN 0 AND 100");
        });
        // CX-01 (PP-22): teto de dinheiro no caixa, opcional (nulo = sem teto).
        b.Property(e => e.CashCeilingAmount).HasPrecision(18, 2);
        // IMP-01 (PP-05): modo fiscal da loja (padrão NFC-e, ou seja, o não fiscal desligado) e o aceite do Proprietário; o cupom pós-venda.
        b.Property(e => e.FiscalMode).HasConversion<string>().HasMaxLength(16).HasDefaultValue(StoreFiscalMode.Nfce).HasSentinel(StoreFiscalMode.Nfce);
        b.Property(e => e.ReceiptPrintMode).HasConversion<string>().HasMaxLength(16).HasDefaultValue(ReceiptPrintMode.Ask).HasSentinel(ReceiptPrintMode.Ask);
        // OFF-01 (PP-10 e PP-22): parâmetros da loja com os padrões do PO; as lojas que já existiam recebem os padrões na migration.
        b.Property(e => e.OfflineWindowHours).HasDefaultValue(Store.DefaultOfflineWindowHours);
        b.Property(e => e.CashBreakTolerance).HasPrecision(18, 2).HasDefaultValue(5.00m);
        b.Property(e => e.ExchangeDays).HasDefaultValue(30);
        b.Property(e => e.FreeRemoveLast).HasDefaultValue(true).HasSentinel(true);
        // PDV-04 (PP-38): limite de desconto da loja, em %; o padrão é da PP-22 (em aberto), até lá 0.
        b.Property(e => e.DiscountLimitPercent).HasPrecision(5, 2).HasDefaultValue(0m);
        b.Property(e => e.SyncAlertHours).HasDefaultValue(Store.DefaultSyncAlertHours);
        b.Property(e => e.CashDropAlertAmount).HasPrecision(18, 2).HasDefaultValue(1000.00m);
        b.Property(e => e.Code).HasMaxLength(6);
        b.Property(e => e.Name).HasMaxLength(Store.MaxNameLength);
        b.Property(e => e.SeedKey).HasMaxLength(40);
        b.HasIndex(e => e.Code).IsUnique().HasDatabaseName(StoreService.StoreCodeIndex);
        b.HasIndex(e => e.SeedKey).IsUnique().HasFilter("seed_key IS NOT NULL");
    }
}

/// <summary>
/// <c>pos.registers</c> (PDV-01 CA 3 e 6): número único na loja, conta Caixa única entre os caixas e um aparelho ativo por caixa (índice único
/// parcial); <c>device_epoch</c> sobe a cada troca de aparelho.
/// </summary>
internal sealed class RegisterConfiguration : IEntityTypeConfiguration<Register>
{
    public void Configure(EntityTypeBuilder<Register> b)
    {
        b.ToTable("registers", PosSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_registers_number", "number BETWEEN 1 AND 99");
            t.HasCheckConstraint("ck_registers_epoch", "device_epoch >= 0");
        });
        b.Property(e => e.UiMode).HasConversion<string>().HasMaxLength(16);
        b.HasOne<Store>().WithMany().HasForeignKey(e => e.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.StoreId, e.Number }).IsUnique().HasDatabaseName(StoreService.RegisterNumberIndex);
        b.HasIndex(e => e.CashAccountId).IsUnique().HasDatabaseName(StoreService.RegisterCashAccountIndex);
        b.HasIndex(e => e.ActiveDeviceId).IsUnique().HasFilter("active_device_id IS NOT NULL");
    }
}

/// <summary>
/// <c>pos.catalog_items</c> (OFF-01): chave (loja, coleção, entidade); o delta lê pelo índice (loja, <c>change_seq</c>). <c>change_seq</c> vem da
/// sequência <c>pos.catalog_change_seq</c>; o <c>payload</c> é jsonb. Sem FK para a loja de propósito: é modelo de leitura, refeito pela carga.
/// </summary>
internal sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public void Configure(EntityTypeBuilder<CatalogItem> b)
    {
        b.ToTable("catalog_items", PosSchema.Name);
        b.HasKey(e => new { e.StoreId, e.Kind, e.EntityId });
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Payload).HasColumnType("jsonb");
        b.Property(e => e.ChangeSeq).HasDefaultValueSql("nextval('pos.catalog_change_seq')");
        b.HasIndex(e => new { e.StoreId, e.ChangeSeq }).HasDatabaseName("ix_catalog_items_store_change_seq");
    }
}

/// <summary><c>pos.printers</c> (IMP-01 CA 6 e 7): impressora do caixa; largura e colunas conferidas por CHECK; uma padrão por caixa.</summary>
internal sealed class PrinterConfiguration : IEntityTypeConfiguration<Printer>
{
    public void Configure(EntityTypeBuilder<Printer> b)
    {
        b.ToTable("printers", PosSchema.Name, t =>
            t.HasCheckConstraint("ck_printers_layout", "(paper_width_mm = 58 AND columns = 32) OR (paper_width_mm = 80 AND columns IN (42, 48))"));
        b.Property(e => e.Name).HasMaxLength(Printer.MaxNameLength);
        b.Property(e => e.Model).HasMaxLength(Printer.MaxModelLength);
        b.Property(e => e.Connection).HasConversion<string>().HasMaxLength(16);
        b.HasOne<Register>().WithMany().HasForeignKey(e => e.RegisterId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Store>().WithMany().HasForeignKey(e => e.StoreId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.RegisterId).IsUnique().HasFilter("is_default AND is_active").HasDatabaseName(PrinterService.DefaultPrinterIndex);
    }
}

/// <summary><c>pos.print_records</c> (IMP-01 CA 4): impressões e reimpressões vindas do aparelho; o id é do aparelho.</summary>
internal sealed class PrintRecordConfiguration : IEntityTypeConfiguration<PrintRecord>
{
    public void Configure(EntityTypeBuilder<PrintRecord> b)
    {
        b.ToTable("print_records", PosSchema.Name, t => t.HasCheckConstraint("ck_print_records_copy", "copy >= 1"));
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.SaleNumber).HasMaxLength(40);
        b.HasOne<Register>().WithMany().HasForeignKey(e => e.RegisterId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.SaleId).HasFilter("sale_id IS NOT NULL");
        b.HasIndex(e => new { e.StoreId, e.PrintedAt });
    }
}

/// <summary><c>pos.catalog_store_builds</c> (OFF-01): a carga inicial de cada loja e a tabela de preço para a qual foi feita.</summary>
internal sealed class CatalogStoreBuildConfiguration : IEntityTypeConfiguration<CatalogStoreBuild>
{
    public void Configure(EntityTypeBuilder<CatalogStoreBuild> b)
    {
        b.ToTable("catalog_store_builds", PosSchema.Name);
        b.HasKey(e => e.StoreId);
    }
}

/// <summary>
/// <c>pos.cash_sessions</c> (CX-01): o id vem do aparelho; uma sessão <c>Open</c> por caixa (índice único parcial, CA 2). FK para o caixa (mesmo
/// schema); o operador é só o id (usuário do Identity).
/// </summary>
internal sealed class CashSessionConfiguration : IEntityTypeConfiguration<CashSession>
{
    public void Configure(EntityTypeBuilder<CashSession> b)
    {
        b.ToTable("cash_sessions", PosSchema.Name, t => t.HasCheckConstraint("ck_cash_sessions_opening", "opening_amount >= 0"));
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.OpeningAmount).HasPrecision(18, 2);
        // CX-02: contagem, recolhimento, revisão e reabertura.
        b.Property(e => e.KeptAmount).HasPrecision(18, 2);
        b.Property(e => e.DepositAmount).HasPrecision(18, 2);
        b.Property(e => e.ReviewReasons).HasDefaultValue(CashReviewReasons.None).HasSentinel(CashReviewReasons.None);
        b.Property(e => e.ApprovalReason).HasMaxLength(CashSessionQueries.MaxReasonLength);
        b.Property(e => e.ReopenReason).HasMaxLength(CashSessionQueries.MaxReasonLength);
        b.Property(e => e.ReopenCount).HasDefaultValue(0);
        b.HasOne<Register>().WithMany().HasForeignKey(e => e.RegisterId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.RegisterId, e.OpenedAt });
        b.HasIndex(e => e.RegisterId).IsUnique().HasFilter("status = 'Open'").HasDatabaseName(CashSessionIngestion.OpenSessionIndex);
        b.HasIndex(e => new { e.StoreId, e.OpenedAt });
    }
}

/// <summary>
/// <c>pos.cash_movements</c> (CX-01): fundo de troco, sangria e suprimento; o id vem do aparelho (reenviar não duplica). Sangria e suprimento com
/// valor positivo e motivo (CHECK).
/// </summary>
internal sealed class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
{
    public void Configure(EntityTypeBuilder<CashMovement> b)
    {
        b.ToTable("cash_movements", PosSchema.Name, t =>
        {
            t.HasCheckConstraint("ck_cash_movements_amount", "amount >= 0 AND (kind = 'Opening' OR amount > 0)");
            t.HasCheckConstraint("ck_cash_movements_reason", "kind = 'Opening' OR reason IS NOT NULL");
        });
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.Reason).HasMaxLength(CashMovement.MaxReasonLength);
        b.HasOne<CashSession>().WithMany().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.SessionId);
    }
}

/// <summary>
/// <c>pos.cash_counts</c> (CX-02): uma linha por forma em cada contagem (id da contagem do aparelho); o esperado e a diferença são do servidor.
/// </summary>
internal sealed class CashCountConfiguration : IEntityTypeConfiguration<CashCount>
{
    public void Configure(EntityTypeBuilder<CashCount> b)
    {
        b.ToTable("cash_counts", PosSchema.Name, t => t.HasCheckConstraint("ck_cash_counts_counted", "counted >= 0"));
        b.Property(e => e.MethodType).HasMaxLength(24);
        b.Property(e => e.Counted).HasPrecision(18, 2);
        b.Property(e => e.Expected).HasPrecision(18, 2);
        b.Property(e => e.PixNotFound).HasPrecision(18, 2);
        b.Property(e => e.Difference).HasPrecision(18, 2);
        b.Property(e => e.Denominations).HasColumnType("jsonb");
        b.HasOne<CashSession>().WithMany().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.SessionId, e.CountId });
    }
}

/// <summary><c>pos.cash_pix_checks</c> (CX-02 CA 13): uma conferência por pagamento Pix da sessão.</summary>
internal sealed class CashPixCheckConfiguration : IEntityTypeConfiguration<CashPixCheck>
{
    public void Configure(EntityTypeBuilder<CashPixCheck> b)
    {
        b.ToTable("cash_pix_checks", PosSchema.Name);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.HasOne<CashSession>().WithMany().HasForeignKey(e => e.SessionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.SessionId, e.PaymentId }).IsUnique();
    }
}

/// <summary>
/// <c>pos.device_events</c> (OFF-02 CA 3): chave no <c>event_id</c> do aparelho; a sequência por caixa e época indexada (não única: um seq repetido
/// de outro evento também é guardado, em quarentena). Imutável por trigger (a migration cria): UPDATE e DELETE falham.
/// </summary>
internal sealed class DeviceEventConfiguration : IEntityTypeConfiguration<DeviceEvent>
{
    public void Configure(EntityTypeBuilder<DeviceEvent> b)
    {
        b.ToTable("device_events", PosSchema.Name);
        b.HasKey(e => e.EventId);
        b.Property(e => e.EventId).ValueGeneratedNever();
        b.Property(e => e.Kind).HasMaxLength(40);
        b.Property(e => e.PrevHash).HasMaxLength(64);
        b.Property(e => e.Hash).HasMaxLength(64);
        b.Property(e => e.Payload).HasColumnType("text");
        b.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(24);
        b.HasIndex(e => new { e.RegisterId, e.DeviceEpoch, e.Seq }).HasDatabaseName("ix_device_events_register_epoch_seq");
        b.HasIndex(e => new { e.RegisterId, e.ReceivedAt });
    }
}

/// <summary><c>pos.sync_divergences</c> (OFF-02 CA 14 e 15): as pendências abertas por loja e tipo; detalhe em jsonb.</summary>
internal sealed class SyncDivergenceConfiguration : IEntityTypeConfiguration<SyncDivergence>
{
    public void Configure(EntityTypeBuilder<SyncDivergence> b)
    {
        b.ToTable("sync_divergences", PosSchema.Name);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Resolution).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.EventKind).HasMaxLength(40);
        b.Property(e => e.SaleNumber).HasMaxLength(40);
        b.Property(e => e.Summary).HasMaxLength(SyncDivergence.MaxSummaryLength);
        b.Property(e => e.ResolutionReason).HasMaxLength(SyncDivergence.MaxReasonLength);
        b.Property(e => e.Details).HasColumnType("jsonb");
        b.HasIndex(e => new { e.Status, e.StoreId, e.Kind });
        b.HasIndex(e => e.EventId);
    }
}

/// <summary><c>pos.operator_exceptions</c>: o relatório de exceções por operador (remoção, redução, descarte, recusa).</summary>
internal sealed class OperatorIncidentConfiguration : IEntityTypeConfiguration<OperatorIncident>
{
    public void Configure(EntityTypeBuilder<OperatorIncident> b)
    {
        b.ToTable("operator_incidents", PosSchema.Name);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.SaleNumber).HasMaxLength(40);
        b.HasIndex(e => new { e.OccurredAt, e.StoreId });
        b.HasIndex(e => e.EventId);
    }
}

/// <summary><c>pos.supervisor_approvals</c> (PDV-04 CA 10): a trilha das aprovações por PIN de supervisor no balcão; supervisor nunca é o operador.</summary>
internal sealed class SupervisorApprovalConfiguration : IEntityTypeConfiguration<SupervisorApproval>
{
    public void Configure(EntityTypeBuilder<SupervisorApproval> b)
    {
        b.ToTable("supervisor_approvals", PosSchema.Name, t => t.HasCheckConstraint("ck_supervisor_approvals_other_person", "supervisor_id <> operator_id"));
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Action).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Reason).HasMaxLength(SupervisorApproval.MaxReasonLength);
        b.Property(e => e.RefundReference).HasMaxLength(SupervisorApproval.MaxRefundLength);
        b.Property(e => e.BeforeAmount).HasPrecision(18, 2);
        b.Property(e => e.AfterAmount).HasPrecision(18, 2);
        b.HasIndex(e => new { e.ApprovedAt, e.StoreId });
        b.HasIndex(e => e.SaleId);
    }
}

/// <summary><c>pos.session_sales</c>: o dinheiro que cada venda deixou na sessão de caixa (sangria acima do calculado, CX-01).</summary>
internal sealed class SessionSaleConfiguration : IEntityTypeConfiguration<SessionSale>
{
    public void Configure(EntityTypeBuilder<SessionSale> b)
    {
        b.ToTable("session_sales", PosSchema.Name);
        b.HasKey(e => e.SaleId);
        b.Property(e => e.SaleId).ValueGeneratedNever();
        b.Property(e => e.CashAmount).HasPrecision(18, 2);
        b.HasIndex(e => e.SessionId);
    }
}
