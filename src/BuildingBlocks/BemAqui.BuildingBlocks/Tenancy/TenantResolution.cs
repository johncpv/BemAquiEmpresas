using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Tenancy;

/// <summary>Tenant resolvido: dados públicos, host primário (para links absolutos) e a conexão ainda cifrada.</summary>
public sealed record ResolvedTenant(TenantInfo Tenant, string LegalName, string? TradeName, string PrimaryHost, string ConnectionCipher, string? SchemaVersion = null);

/// <summary>Resolve o tenant pelo host ou pelo id, com cache; <see cref="Invalidate"/> é chamado ao mudar o catálogo.</summary>
public interface ITenantResolver
{
    Task<ResolvedTenant?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default);

    Task<ResolvedTenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);

    void Invalidate();
}

/// <summary>Contexto de tenant do escopo, preenchido pelo middleware (request) ou pela fábrica de escopo (job/outbox).</summary>
public sealed class TenantContextHolder : ITenantContext
{
    public TenantInfo? Tenant => Resolved?.Tenant;

    public ResolvedTenant? Resolved { get; private set; }

    public void Set(ResolvedTenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        if (Resolved is not null && Resolved.Tenant.Id != tenant.Tenant.Id)
        {
            throw new InvalidOperationException("O escopo já pertence a outro tenant.");
        }

        Resolved = tenant;
    }
}

/// <summary>Escopo novo já ligado ao tenant: o TenantDbContext do escopo usa o banco dele (FND-02 CA 8).</summary>
public sealed class TenantScopeFactory(IServiceScopeFactory scopes, ITenantResolver resolver) : ITenantScopeFactory
{
    public async Task<AsyncServiceScope> CreateScopeAsync(TenantInfo tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var resolved = await resolver.FindByIdAsync(tenant.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenant.Id} não encontrado no catálogo.");

        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContextHolder>().Set(resolved);
        return scope;
    }
}
