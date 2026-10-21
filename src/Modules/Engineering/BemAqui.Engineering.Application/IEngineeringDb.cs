using BemAqui.Engineering.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace BemAqui.Engineering.Application;

/// <summary>Dados do módulo Engineering (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IEngineeringDb
{
    DbSet<RoutingTemplate> RoutingTemplates { get; }

    DbSet<RoutingTemplateStep> RoutingTemplateSteps { get; }

    DbSet<RoutingTemplateItem> RoutingTemplateItems { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}