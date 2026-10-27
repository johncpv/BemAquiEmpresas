using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

/// <summary>
/// Lotes do extrato do banco (BNK-03a CA 5 e 12): o SHA-256 do arquivo é único por conta entre os lotes não desfeitos (o mesmo arquivo
/// recusado inteiro; depois de desfeito, pode entrar de novo).
/// </summary>
internal sealed class BankStatementBatchConfiguration : IEntityTypeConfiguration<BankStatementBatch>
{
    public const string FileIndex = BemAqui.Finance.Application.Statements.BankStatementImportService.FileIndex;

    public void Configure(EntityTypeBuilder<BankStatementBatch> b)
    {
        b.ToTable("bank_statement_batches", FinanceSchema.Name);
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.Source).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Trigger).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.FileName).HasMaxLength(200);
        b.Property(e => e.FileSha256).HasMaxLength(64);
        b.Property(e => e.Notes).HasMaxLength(1000);
        b.Property(e => e.UndoReason).HasMaxLength(200);
        b.Ignore(e => e.IsUndone);
        b.HasIndex(e => new { e.BankAccountId, e.FileSha256 }).IsUnique().HasFilter("file_sha256 IS NOT NULL AND undone_at IS NULL").HasDatabaseName(FileIndex);
        b.HasIndex(e => new { e.BankAccountId, e.ImportedAt });
    }
}

/// <summary>
/// Linhas do extrato do banco (BNK-03a CA 2): única por <c>(conta, origem, external_id)</c> entre as não desfeitas; índice por conta e
/// data para a aba (CA 8: 5 mil linhas em menos de 1 s) e por <c>external_id</c> para conferir o OFX de contingência contra a API (CA 6).
/// </summary>
internal sealed class BankStatementLineConfiguration : IEntityTypeConfiguration<BankStatementLine>
{
    public const string ExternalIndex = BemAqui.Finance.Application.Statements.BankStatementImportService.ExternalIndex;

    public void Configure(EntityTypeBuilder<BankStatementLine> b)
    {
        b.ToTable("bank_statement_lines", FinanceSchema.Name);
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankStatementBatch>().WithMany().HasForeignKey(e => e.BatchId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.Description).HasMaxLength(300);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.CounterpartyName).HasMaxLength(150);
        b.Property(e => e.CounterpartyDocument).HasMaxLength(14);
        b.Property(e => e.ExternalId).HasMaxLength(100);
        b.Property(e => e.Source).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.IgnoreReason).HasMaxLength(200);
        b.Ignore(e => e.IsDebit);
        b.HasIndex(e => new { e.BankAccountId, e.Source, e.ExternalId }).IsUnique().HasFilter("status <> 'Undone'").HasDatabaseName(ExternalIndex);
        b.HasIndex(e => new { e.BankAccountId, e.ExternalId });
        b.HasIndex(e => new { e.BankAccountId, e.Date });
        b.HasIndex(e => e.BatchId);
    }
}

/// <summary>Saldo do banco por dia (CA 3): um por conta, dia e origem (a importação seguinte atualiza).</summary>
internal sealed class BankStatementBalanceConfiguration : IEntityTypeConfiguration<BankStatementBalance>
{
    public void Configure(EntityTypeBuilder<BankStatementBalance> b)
    {
        b.ToTable("bank_statement_balances", FinanceSchema.Name);
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.Balance).HasPrecision(18, 2);
        b.Property(e => e.Source).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(e => new { e.BankAccountId, e.Date, e.Source }).IsUnique();
    }
}

/// <summary>Contingência do OFX numa conta com API (D13, CA 6): histórico de liberações; vale a mais recente no prazo.</summary>
internal sealed class BankOfxContingencyConfiguration : IEntityTypeConfiguration<BankOfxContingency>
{
    public void Configure(EntityTypeBuilder<BankOfxContingency> b)
    {
        b.ToTable("bank_ofx_contingencies", FinanceSchema.Name);
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.Reason).HasMaxLength(200);
        b.HasIndex(e => new { e.BankAccountId, e.ValidUntil });
    }
}
