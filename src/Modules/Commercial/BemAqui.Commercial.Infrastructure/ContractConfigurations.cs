using BemAqui.Commercial.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BemAqui.Commercial.Infrastructure;

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> b)
    {
        b.ToTable("contracts", CommercialSchema.Name);
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(e => e.TotalValue).HasPrecision(18, 2);
        b.Property(e => e.Snapshot).HasColumnType("jsonb");
        b.Property(e => e.CancelReason).HasMaxLength(500);
        b.HasOne<Quote>().WithMany().HasForeignKey(e => e.QuoteId).OnDelete(DeleteBehavior.Restrict);

        // Uma aprovação por orçamento (COM-04a CA 9): a concorrente para no índice, se passar do xmin.
        b.HasIndex(e => e.QuoteId).IsUnique().HasDatabaseName("ix_contracts_quote_id");
        b.HasIndex(e => e.Number).IsUnique();
        b.HasIndex(e => e.PartyId);
    }
}

internal sealed class ContractMilestoneConfiguration : IEntityTypeConfiguration<ContractMilestone>
{
    public void Configure(EntityTypeBuilder<ContractMilestone> b)
    {
        b.ToTable("contract_milestones", CommercialSchema.Name);
        b.Property(e => e.Description).HasMaxLength(120);
        b.Property(e => e.Percent).HasPrecision(7, 4);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.Trigger).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.TriggerEvent).HasConversion<string>().HasMaxLength(24);
        b.HasOne<Contract>().WithMany().HasForeignKey(e => e.ContractId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.ContractId);
    }
}

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.ToTable("projects", CommercialSchema.Name);
        b.Property(e => e.Number).HasMaxLength(20);
        b.Property(e => e.Name).HasMaxLength(200);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(12);
        b.HasOne<Contract>().WithMany().HasForeignKey(e => e.ContractId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.Number).IsUnique();
        b.HasIndex(e => e.ContractId);
        b.HasIndex(e => e.PartyId);
    }
}

/// <summary>
/// Previsão de entrega do projeto (PLN-01b CA 7, ADR 0029): uma linha por projeto (<c>project_id</c> é a chave), fora da auditoria; o motivo é
/// código (até 32). A linha de <c>projects</c> não é tocada.
/// </summary>
internal sealed class ProjectForecastConfiguration : IEntityTypeConfiguration<ProjectForecast>
{
    public void Configure(EntityTypeBuilder<ProjectForecast> b)
    {
        b.ToTable("project_forecasts", CommercialSchema.Name);
        b.HasKey(e => e.ProjectId);
        b.Property(e => e.ProjectId).ValueGeneratedNever();
        b.Property(e => e.Reason).HasMaxLength(32);
        b.HasOne<Project>().WithOne().HasForeignKey<ProjectForecast>(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProjectItemConfiguration : IEntityTypeConfiguration<ProjectItem>
{
    public void Configure(EntityTypeBuilder<ProjectItem> b)
    {
        b.ToTable("project_items", CommercialSchema.Name);
        b.Property(e => e.Description).HasMaxLength(300);
        b.Property(e => e.Quantity).HasPrecision(18, 4);
        b.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<QuoteItem>().WithMany().HasForeignKey(e => e.QuoteItemId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.ProjectId);
        b.HasIndex(e => e.QuoteItemId);
        b.HasIndex(e => e.RoutingTemplateId);
        b.HasIndex(e => e.ItemId);
    }
}

internal sealed class ProjectBudgetConfiguration : IEntityTypeConfiguration<ProjectBudget>
{
    public void Configure(EntityTypeBuilder<ProjectBudget> b)
    {
        b.ToTable("project_budget", CommercialSchema.Name);
        b.Property(e => e.CostType).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.BudgetedAmount).HasPrecision(18, 2);
        b.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ProjectId, e.CostType }).IsUnique();
    }
}

/// <summary>Razão de diferenças (ADR 0025 §2, PRD-04 CA 7): único por <c>(source, source_id, seq)</c>; consulta por projeto e natureza.</summary>
internal sealed class ProjectCostConfiguration : IEntityTypeConfiguration<ProjectCost>
{
    public void Configure(EntityTypeBuilder<ProjectCost> b)
    {
        b.ToTable("project_costs", CommercialSchema.Name);
        b.Property(e => e.CostType).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.Source).HasConversion<string>().HasMaxLength(24);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.InvoiceAmount).HasPrecision(18, 2);
        b.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.Source, e.SourceId, e.Seq }).IsUnique();
        b.HasIndex(e => new { e.ProjectId, e.CostType });
        b.HasIndex(e => e.SourceId);
        // Marca de recomposição do relatório de margem (DSH-02 CA 6 e 18): só as linhas recompostas, poucas, sem varrer a razão inteira.
        b.HasIndex(e => e.ProjectId).HasFilter("is_backfill").HasDatabaseName("ix_project_costs_project_id_backfill");
    }
}

internal sealed class ContractAmendmentConfiguration : IEntityTypeConfiguration<ContractAmendment>
{
    public void Configure(EntityTypeBuilder<ContractAmendment> b)
    {
        b.ToTable("contract_amendments", CommercialSchema.Name);
        b.Property(e => e.Description).HasMaxLength(500);
        b.Property(e => e.ValueDelta).HasPrecision(18, 2);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.BudgetDeltas).HasColumnType("jsonb");
        b.Property(e => e.RejectionComment).HasMaxLength(500);
        b.HasOne<Contract>().WithMany().HasForeignKey(e => e.ContractId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.ContractId, e.Number }).IsUnique();
    }
}

internal sealed class ContractAmendmentMilestoneConfiguration : IEntityTypeConfiguration<ContractAmendmentMilestone>
{
    public void Configure(EntityTypeBuilder<ContractAmendmentMilestone> b)
    {
        b.ToTable("contract_amendment_milestones", CommercialSchema.Name);
        b.Property(e => e.Description).HasMaxLength(120);
        b.Property(e => e.Percent).HasPrecision(7, 4);
        b.Property(e => e.Amount).HasPrecision(18, 2);
        b.Property(e => e.Trigger).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.TriggerEvent).HasConversion<string>().HasMaxLength(24);
        b.HasOne<ContractAmendment>().WithMany().HasForeignKey(e => e.AmendmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => e.AmendmentId);
    }
}

internal sealed class ContractAmendmentReductionConfiguration : IEntityTypeConfiguration<ContractAmendmentReduction>
{
    public void Configure(EntityTypeBuilder<ContractAmendmentReduction> b)
    {
        b.ToTable("contract_amendment_reductions", CommercialSchema.Name);
        b.Property(e => e.NewAmount).HasPrecision(18, 2);
        b.HasOne<ContractAmendment>().WithMany().HasForeignKey(e => e.AmendmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(e => new { e.AmendmentId, e.ReceivableId }).IsUnique();
    }
}
