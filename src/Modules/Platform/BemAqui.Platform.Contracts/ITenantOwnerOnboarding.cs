namespace BemAqui.Platform.Contracts;

/// <summary>
/// Dono do tenant no provisionamento (FND-03a etapas CreateOwner e SendInvite), implementado pelo Identity.
/// Roda no escopo do tenant novo. As duas etapas são idempotentes: rodar de novo não duplica nada.
/// </summary>
public interface ITenantOwnerOnboarding
{
    Task CreateOwnerAsync(Guid tenantId, string tenantKey, string email, IServiceProvider tenantScope, CancellationToken cancellationToken);

    Task SendInviteAsync(Guid tenantId, string tenantKey, string email, IServiceProvider tenantScope, CancellationToken cancellationToken);
}
