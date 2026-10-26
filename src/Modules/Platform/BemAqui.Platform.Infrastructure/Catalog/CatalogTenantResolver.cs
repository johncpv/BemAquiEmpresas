using System.Collections.Concurrent;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Platform.Infrastructure.Catalog;

/// <summary>
/// Resolve o tenant no catálogo com cache em memória: positivo por 5 min, negativo por 30 s e limitado
/// (host aleatório não enche a memória). O LISTEN/NOTIFY do catálogo chama <see cref="Invalidate"/>,
/// então a suspensão vale em segundos (FND-02 CA 4).
/// </summary>
public sealed class CatalogTenantResolver(IServiceScopeFactory scopes, TimeProvider clock) : ITenantResolver, ITenantDirectory
{
    public static readonly TimeSpan PositiveTtl = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan NegativeTtl = TimeSpan.FromSeconds(30);
    public const int MaxNegativeEntries = 1000;

    private readonly ConcurrentDictionary<string, (ResolvedTenant? Tenant, DateTimeOffset Expires)> _byHost = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, (ResolvedTenant? Tenant, DateTimeOffset Expires)> _byId = new();
    private int _negativeCount;

    // Sobe a cada Invalidate: leitura do catálogo que começou antes dele não volta para o cache (senão o módulo desligado
    // ou o tenant suspenso seguiriam liberados por até 5 min, numa corrida com outra resolução em andamento).
    private long _generation;

    public async Task<ResolvedTenant?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default)
    {
        var key = TenantDomain.Normalize(host);
        var now = clock.GetUtcNow();
        if (_byHost.TryGetValue(key, out var cached) && cached.Expires > now)
        {
            return cached.Tenant;
        }

        var generation = Interlocked.Read(ref _generation);
        var tenant = await LoadAsync(q => q.Where(t => t.Domains.Any(d => d.Host == key)), cancellationToken);
        Remember(_byHost, key, tenant, now, generation);
        return tenant;
    }

    public async Task<ResolvedTenant?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        if (_byId.TryGetValue(tenantId, out var cached) && cached.Expires > now)
        {
            return cached.Tenant;
        }

        var generation = Interlocked.Read(ref _generation);
        var tenant = await LoadAsync(q => q.Where(t => t.Id == tenantId), cancellationToken);
        Remember(_byId, tenantId, tenant, now, generation);
        return tenant;
    }

    public async Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        return await db.Tenants.AsNoTracking()
            .Where(t => t.Status == TenantLifecycle.Active)
            .Select(t => new TenantInfo(t.Id, t.Key, TenantStatus.Active))
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        (await FindByIdAsync(tenantId, cancellationToken))?.Tenant;

    public void Invalidate()
    {
        Interlocked.Increment(ref _generation);
        _byHost.Clear();
        _byId.Clear();
        Interlocked.Exchange(ref _negativeCount, 0);
    }

    private async Task<ResolvedTenant?> LoadAsync(Func<IQueryable<Tenant>, IQueryable<Tenant>> filter, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var tenant = await filter(db.Tenants.AsNoTracking().Include(t => t.Domains)).FirstOrDefaultAsync(cancellationToken);
        if (tenant is null)
        {
            return null;
        }

        var primary = tenant.Domains.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Host, StringComparer.Ordinal).FirstOrDefault()?.Host ?? string.Empty;
        var status = tenant.Status switch
        {
            TenantLifecycle.Active => TenantStatus.Active,
            TenantLifecycle.Suspended => TenantStatus.Suspended,
            _ => TenantStatus.Provisioning,
        };
        var released = await db.TenantModules.AsNoTracking().Where(m => m.TenantId == tenant.Id && m.Enabled).Select(m => m.ModuleCode).ToListAsync(cancellationToken);
        return new ResolvedTenant(new TenantInfo(tenant.Id, tenant.Key, status), tenant.LegalName, tenant.TradeName, primary, tenant.ConnectionSecret, tenant.SchemaVersion)
        {
            ReleasedModules = released.ToHashSet(StringComparer.Ordinal),
            BankProductionAllowed = tenant.BankProductionAllowed,
        };
    }

    private void Remember<TKey>(ConcurrentDictionary<TKey, (ResolvedTenant? Tenant, DateTimeOffset Expires)> cache, TKey key, ResolvedTenant? tenant, DateTimeOffset now, long generation)
        where TKey : notnull
    {
        if (Interlocked.Read(ref _generation) != generation)
        {
            return; // houve Invalidate durante a leitura: o próximo pedido lê de novo
        }

        if (tenant is null)
        {
            if (Interlocked.Increment(ref _negativeCount) > MaxNegativeEntries)
            {
                return; // sem cache negativo além do limite: protege a memória contra hosts aleatórios
            }

            cache[key] = (null, now + NegativeTtl);
            return;
        }

        cache[key] = (tenant, now + PositiveTtl);
    }
}
