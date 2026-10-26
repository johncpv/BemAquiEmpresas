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

    DbSet<ProjectCost> ProjectCosts { get; }

    /// <summary>Previsão de entrega do projeto (PLN-01b), escrita só pelo consumidor do <c>ProjectForecastChanged</c>.</summary>
    DbSet<ProjectForecast> ProjectForecasts { get; }

    DbSet<ContractAmendment> ContractAmendments { get; }

    DbSet<ContractAmendmentMilestone> ContractAmendmentMilestones { get; }

    DbSet<ContractAmendmentReduction> ContractAmendmentReductions { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
