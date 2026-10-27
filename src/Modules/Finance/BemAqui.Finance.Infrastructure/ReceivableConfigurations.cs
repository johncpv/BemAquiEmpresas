using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Finance.Infrastructure;

internal sealed class ReceivableConfiguration : IEntityTypeConfiguration<Receivable>
{
    public void Configure(EntityTypeBuilder<Receivable> b)
    {
        b.ToTable("receivables", FinanceSchema.Name);
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.Description).HasMaxLength(200);
        foreach (var money in new[] { nameof(Receivable.Amount), nameof(Receivable.PaidAmount), nameof(Receivable.Interest), nameof(Receivable.Fine), nameof(Receivable.Discount) })
        {
            b.Property(money).HasPrecision(18, 2);
        }

        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Origin).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.CancelReason).HasMaxLength(200);
        b.Property(e => e.OpeningReference).HasMaxLength(40);
        b.Ignore(e => e.Balance);
        b.Ignore(e => e.IsOpen);
        b.HasIndex(e => e.Number).IsUnique().HasDatabaseName(ReceivableService.NumberIndex);
        b.HasIndex(e => new { e.Status, e.DueDate }).HasFilter("status NOT IN ('Paid','Cancelled')").HasDatabaseName("ix_receivables_open_due");
        b.HasIndex(e => e.PartyId);
        b.HasIndex(e => e.InstallmentGroupId);
        b.HasIndex(e => new { e.Origin, e.OriginId });
        b.Property(e => e.TriggerEvent).HasConversion<string>().HasMaxLength(24);

        // Uma parcela por marco do contrato (COM-04b CA 3): a reentrega do ContractSigned para aqui.
        b.HasIndex(e => e.OriginId).IsUnique().HasFilter("origin = 'Contract'").HasDatabaseName(ReceivableService.ContractMilestoneIndex);
        b.HasIndex(e => e.ContractId);
        b.HasIndex(e => e.ProjectId);
        b.HasIndex(e => e.BillingSlipId).HasFilter("billing_slip_id IS NOT NULL");

        // DRE por competência (FIN-08 CA 17): agregação por conta e data de emissão.
        b.HasIndex(e => new { e.IssueDate, e.AccountId }).IncludeProperties(e => e.Amount).HasDatabaseName("ix_receivables_issue_date");
    }
}

internal sealed class ProjectTriggerEventConfiguration : IEntityTypeConfiguration<ProjectTriggerEvent>
{
    public void Configure(EntityTypeBuilder<ProjectTriggerEvent> b)
    {
        b.ToTable("project_trigger_events", FinanceSchema.Name);
        b.Property(e => e.Event).HasConversion<string>().HasMaxLength(24);
        b.HasIndex(e => new { e.ProjectId, e.Event }).IsUnique().HasDatabaseName(ReceivableService.TriggerEventIndex);
    }
}

internal sealed class ReceivableSettlementConfiguration : IEntityTypeConfiguration<ReceivableSettlement>
{
    public void Configure(EntityTypeBuilder<ReceivableSettlement> b)
    {
        b.ToTable("receivable_settlements", FinanceSchema.Name);
        foreach (var money in new[] { nameof(ReceivableSettlement.Principal), nameof(ReceivableSettlement.Interest), nameof(ReceivableSettlement.Fine), nameof(ReceivableSettlement.Discount), nameof(ReceivableSettlement.Total), nameof(ReceivableSettlement.BalanceBefore) })
        {
            b.Property(money).HasPrecision(18, 2);
        }

        b.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.BackdateReason).HasMaxLength(200);
        b.Property(e => e.ReversalReason).HasMaxLength(200);
        b.HasOne<Receivable>().WithMany().HasForeignKey(e => e.ReceivableId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.ReceivableId);
        b.HasIndex(e => e.Date);
    }
}

internal sealed class FinancialTransactionConfiguration : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> b)
    {
        b.ToTable("financial_transactions", FinanceSchema.Name);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Description).HasMaxLength(200);
        b.HasOne<BankAccount>().WithMany().HasForeignKey(e => e.BankAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.BankAccountId, e.Date }).IncludeProperties(e => e.Amount);
        b.HasIndex(e => e.PayableId);
        b.HasIndex(e => e.TransferId);
        b.HasIndex(e => e.BankEntryId);
        b.HasIndex(e => e.ReceivableId);
    }
}

internal sealed class FinanceParameterConfiguration : IEntityTypeConfiguration<FinanceParameter>
{
    public void Configure(EntityTypeBuilder<FinanceParameter> b)
    {
        b.ToTable("parameters", FinanceSchema.Name);
        b.Property(e => e.Key).HasMaxLength(64);
        b.HasIndex(e => e.Key).IsUnique();
        b.Property(e => e.Value).HasPrecision(18, 4);
        b.Property(e => e.Text).HasMaxLength(64);
        b.Property(e => e.ReviewState).HasConversion<string>().HasMaxLength(16);
    }
}

/// <summary>Marca da última consulta do DRE (FIN-08 CA 9): uma por usuário, fora da auditoria.</summary>
internal sealed class DreViewMarkConfiguration : IEntityTypeConfiguration<DreViewMark>
{
    public void Configure(EntityTypeBuilder<DreViewMark> b)
    {
        b.ToTable("dre_view_marks", FinanceSchema.Name);
        b.HasIndex(e => e.UserId).IsUnique().HasDatabaseName(DreService.ViewMarkIndex);
    }
}
