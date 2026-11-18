using BemAqui.Pos.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Pos.Application;

/// <summary>Dados do módulo Pos (ADR 0006): os handlers do módulo só enxergam estas tabelas (schema <c>pos</c>).</summary>
public interface IPosDb
{
    DbSet<Store> Stores { get; }

    DbSet<Register> Registers { get; }

    /// <summary>Modelo de leitura do caixa (OFF-01).</summary>
    DbSet<CatalogItem> CatalogItems { get; }

    /// <summary>Carga inicial do catálogo de cada loja (OFF-01).</summary>
    DbSet<CatalogStoreBuild> CatalogStoreBuilds { get; }

    /// <summary>Gravação em lote do modelo de leitura e a trava do tenant (OFF-01).</summary>
    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Schema do módulo.</summary>
public static class PosSchema
{
    public const string Name = "pos";
}
