using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;

namespace BemAqui.Dashboard.Application;

/// <summary>
/// Quem pede (tenant, usuário, permissão efetiva) e o cache de 60 s do painel (ADR 0028 §3), para os serviços da DSH-01b: o card só lê a
/// permissão do dono com o módulo dele liberado, e o cache separa por usuário, versão de permissão e geração do tenant.
/// </summary>
public sealed class DashboardAccess(DashboardCache cache, ICurrentUser currentUser, IPermissionService permissions, TenantContextHolder tenant)
{
    private EffectivePermissions? _effective;

    /// <summary>A permissão do dono com o módulo dela liberado no tenant.</summary>
    public async Task<bool> CanAsync(string permission, CancellationToken cancellationToken = default) =>
        tenant.Resolved?.IsReleased(CardRule.ModuleOf(permission)) == true && (await EffectiveAsync(cancellationToken)).Has(permission);

    public async Task<T> CachedAsync<T>(string card, string parameters, Func<CancellationToken, Task<T>> compute, CancellationToken cancellationToken)
        where T : class
    {
        var scope = new CardScope(TenantId, UserId, (await EffectiveAsync(cancellationToken)).Version);
        var entry = await cache.GetOrComputeAsync(scope, card, parameters, async ct => await compute(ct), cancellationToken);
        return (T)entry.Value;
    }

    private async Task<EffectivePermissions> EffectiveAsync(CancellationToken cancellationToken) =>
        _effective ??= await permissions.GetAsync(UserId, cancellationToken);

    private Guid TenantId => tenant.Tenant?.Id ?? throw new InvalidOperationException("Painel sem tenant no escopo.");

    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("Painel sem usuário autenticado.");
}
