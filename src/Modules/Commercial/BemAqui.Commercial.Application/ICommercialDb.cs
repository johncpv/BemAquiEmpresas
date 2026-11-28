using BemAqui.Commercial.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Commercial.Application;

/// <summary>Dados do módulo Commercial (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface ICommercialDb
{
    DbSet<Quote> Quotes { get; }

    DbSet<QuoteItem> QuoteItems { get; }

    DbSet<QuoteItemCost> QuoteItemCosts { get; }

    DbSet<QuoteMilestone> QuoteMilestones { get; }

    DbSet<QuoteDispatch> QuoteDispatches { get; }

    DbSet<CommercialParameter> CommercialParameters { get; }

    DbSet<Contract> Contracts { get; }

    DbSet<ContractMilestone> ContractMilestones { get; }

    DbSet<Project> Projects { get; }

    DbSet<ProjectItem> ProjectItems { get; }

    DbSet<ProjectBudget> ProjectBudgets { get; }

    /// <summary>Edições diretas do custo orçado (COM-09): só inclusão.</summary>
    DbSet<ProjectBudgetRevision> ProjectBudgetRevisions { get; }

    /// <summary>Aceites do cliente no projeto de serviço (EXE-01a): só inclusão; o desfazer marca a linha.</summary>
    DbSet<ProjectAcceptance> ProjectAcceptances { get; }

    DbSet<ProjectCost> ProjectCosts { get; }

    /// <summary>Previsão de entrega do projeto (PLN-01b), escrita só pelo consumidor do <c>ProjectForecastChanged</c>.</summary>
    DbSet<ProjectForecast> ProjectForecasts { get; }

    DbSet<ContractAmendment> ContractAmendments { get; }

    DbSet<ContractAmendmentMilestone> ContractAmendmentMilestones { get; }

    DbSet<ContractAmendmentReduction> ContractAmendmentReductions { get; }

    /// <summary>Modelos de etapas do projeto de serviço (EXE-02), com as etapas e os itens de serviço vinculados.</summary>
    DbSet<StageTemplate> StageTemplates { get; }

    DbSet<StageTemplateStep> StageTemplateSteps { get; }

    DbSet<StageTemplateItem> StageTemplateItems { get; }

    /// <summary>Etapas do projeto de serviço e o checklist de cada uma (EXE-03a).</summary>
    DbSet<ProjectServiceStage> ProjectServiceStages { get; }

    DbSet<ProjectServiceStageCheck> ProjectServiceStageChecks { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
