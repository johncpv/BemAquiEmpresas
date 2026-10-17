using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Tenancy;

public enum TenantStatus
{
    Provisioning,
    Active,
    Suspended,
}

public sealed record TenantInfo(Guid Id, string Key, TenantStatus Status);

/// <summary>Catálogo de tenants visto pela infraestrutura (implementação real na FND-02).</summary>
public interface ITenantDirectory
{
    Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Abre um escopo de DI já apontado para o banco de um tenant (o <c>TenantDbContext</c> do escopo usa a conexão dele).
/// Jobs e dispatcher nunca reaproveitam escopo de outro tenant (FND-07 CA 15 e 16).
/// </summary>
public interface ITenantScopeFactory
{
    Task<AsyncServiceScope> CreateScopeAsync(TenantInfo tenant, CancellationToken cancellationToken = default);
}

/// <summary>Tenant do escopo atual (request, job ou dispatcher).</summary>
public interface ITenantContext
{
    TenantInfo? Tenant { get; }
}
