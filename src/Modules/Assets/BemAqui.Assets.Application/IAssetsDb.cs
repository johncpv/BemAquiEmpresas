using BemAqui.Assets.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BemAqui.Assets.Application;

/// <summary>Dados do módulo Assets (ADR 0006): os handlers do módulo só enxergam estas tabelas (schema <c>assets</c>).</summary>
public interface IAssetsDb
{
    DbSet<Asset> Assets { get; }

    DbSet<AssetSite> AssetSites { get; }

    /// <summary>Transação da criação: a etiqueta <c>PAT</c> volta se o bem não for salvo (sem lacuna).</summary>
    DatabaseFacade Database { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Quem dá histórico ao bem (PAT-01a CA 12): movimentação (PAT-02), manutenção e custo (PAT-03a/03b). Com uso, o bem não é excluído;
/// usa "Baixar bem".
/// </summary>
public interface IAssetUsageProvider
{
    /// <summary>Descrição curta do uso ("movimentações") ou null se não usa.</summary>
    Task<string?> UsageAsync(Guid assetId, CancellationToken cancellationToken = default);
}
