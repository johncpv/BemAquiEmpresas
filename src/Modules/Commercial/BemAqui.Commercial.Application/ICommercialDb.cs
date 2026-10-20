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

    DbSet<CommercialParameter> CommercialParameters { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
