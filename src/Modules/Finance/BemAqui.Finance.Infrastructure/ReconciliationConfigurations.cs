using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

/// <summary>Conciliações (BNK-03b): por conta e data; nunca apagadas (o desfazer só marca).</summary>
internal sealed class BankReconciliationConfiguration : IEntityTypeConfiguration<BankReconciliation>
{
    public void Configure(EntityTypeBuilder<BankReconciliation> b)
    {
        b.ToTable("bank_reconciliations", FinanceSchema.Name);
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Confidence).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.Reason).HasMaxLength(200);
        b.Property(e => e.ClosedPeriodReason).HasMaxLength(200);
        b.Property(e => e.UndoReason).HasMaxLength(200);
        b.Ignore(e => e.IsUndone);
        b.HasIndex(e => new { e.BankAccountId, e.CreatedAt });
    }
}

/// <summary>Vínculos da conciliação: por linha e por transação (o histórico de cada uma, CA 15).</summary>
internal sealed class BankReconciliationLinkConfiguration : IEntityTypeConfiguration<BankReconciliationLink>
{
    public void Configure(EntityTypeBuilder<BankReconciliationLink> b)
    {
        b.ToTable("bank_reconciliation_links", FinanceSchema.Name);
        b.HasOne<BankReconciliation>().WithMany().HasForeignKey(e => e.ReconciliationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<BankStatementLine>().WithMany().HasForeignKey(e => e.StatementLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinancialTransaction>().WithMany().HasForeignKey(e => e.TransactionId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.HasIndex(e => e.ReconciliationId);
        b.HasIndex(e => e.StatementLineId);
        b.HasIndex(e => e.TransactionId);
    }
}

internal sealed class BankStatementRuleConfiguration : IEntityTypeConfiguration<BankStatementRule>
{
    public void Configure(EntityTypeBuilder<BankStatementRule> b)
    {
        b.ToTable("bank_statement_rules", FinanceSchema.Name);
        b.Property(e => e.Keyword).HasMaxLength(60);
        b.Property(e => e.LineKind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.EntryKind).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(e => e.IsActive);
    }
}

/// <summary>Fechamentos: o mais recente da conta define até quando a conciliação está fechada.</summary>
internal sealed class BankReconciliationClosingConfiguration : IEntityTypeConfiguration<BankReconciliationClosing>
{
    public void Configure(EntityTypeBuilder<BankReconciliationClosing> b)
    {
        b.ToTable("bank_reconciliation_closings", FinanceSchema.Name);
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.Property(e => e.BankBalance).HasPrecision(18, 2);
        b.Property(e => e.SystemBalance).HasPrecision(18, 2);
        b.HasIndex(e => new { e.BankAccountId, e.UntilDate });
    }
}
