using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Tenancy;

/// <summary>
/// Tenant resolvido: dados públicos, host primário (para links absolutos), a conexão ainda cifrada e os módulos
/// liberados (FND-06). Recarrega quando o catálogo avisa por NOTIFY, então mudar a liberação vale no próximo request.
/// </summary>
public sealed record ResolvedTenant(TenantInfo Tenant, string LegalName, string? TradeName, string PrimaryHost, string ConnectionCipher, string? SchemaVersion = null)
{
    public IReadOnlySet<string> ReleasedModules { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public bool IsReleased(string moduleCode) => ReleasedModules.Contains(moduleCode);

    /// <summary>
    /// Módulos fora de todos os perfis de negócio do tenant e não liberados (PERF-01, ADR 0046): somem do menu, da busca e da ajuda.
    /// Só apresentação: quem barra é <see cref="ReleasedModules"/> (camada 1).
    /// </summary>
    public IReadOnlySet<string> HiddenModules { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    public bool IsHidden(string moduleCode) => HiddenModules.Contains(moduleCode);

    /// <summary>Perfis de negócio ativos do tenant (ADR 0046): decidem os recursos por perfil dentro de um módulo comum (adendo da PAG-01).</summary>
    public IReadOnlySet<string> Profiles { get; init; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Recurso por perfil liberado: o tenant tem algum dos perfis do recurso (<see cref="Modules.BusinessProfileCatalog.Features"/>).</summary>
    public bool HasFeature(string feature) => Modules.BusinessProfileCatalog.HasFeature(Profiles, feature);

    /// <summary>
    /// Vocabulário dos perfis do tenant (PERF-03, ADR 0046 §6), só com o que difere do padrão: a fábrica recebe vazio. Vem de
    /// <c>tenant_profiles</c>, então troca de perfil chega pelo mesmo NOTIFY que recarrega o tenant.
    /// </summary>
    public IReadOnlyDictionary<string, string> Vocabulary { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Cobrança real pelo banco liberada (<c>catalog.tenants.bank_production_allowed</c>, ligada só pelo super admin na GOL-01b, ADR 0034):
    /// sem ela, a integração bancária de produção só é salva e testada (BNK-01a CA 12).
    /// </summary>
    public bool BankProductionAllowed { get; init; }
}

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
