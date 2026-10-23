using BemAqui.Engineering.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Engineering.Application;

/// <summary>Dados do módulo Engineering (ADR 0006): os handlers do módulo só enxergam estas tabelas.</summary>
public interface IEngineeringDb
{
    DbSet<RoutingTemplate> RoutingTemplates { get; }

    DbSet<RoutingTemplateStep> RoutingTemplateSteps { get; }

    DbSet<RoutingTemplateItem> RoutingTemplateItems { get; }

    DbSet<Bom> Boms { get; }

    DbSet<BomLine> BomLines { get; }

    /// <summary>Para liberar a revisão em dois passos numa transação (a anterior sai de Released antes da nova entrar, ENG-02 CA 13).</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;
}
