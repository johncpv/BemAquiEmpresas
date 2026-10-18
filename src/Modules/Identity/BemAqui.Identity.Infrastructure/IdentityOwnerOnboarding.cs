using BemAqui.Identity.Application;
using BemAqui.Platform.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Identity.Infrastructure;

/// <summary>
/// Etapas CreateOwner e SendInvite do provisionamento (FND-04 CA 19): o proprietário nasce como convite pendente
/// no banco do tenant novo e recebe o e-mail de convite. As duas são idempotentes.
/// </summary>
public sealed class IdentityOwnerOnboarding : ITenantOwnerOnboarding
{
    public async Task CreateOwnerAsync(Guid tenantId, string tenantKey, string email, IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        await tenantScope.GetRequiredService<Invites>().EnsureInviteAsync(email, isOwner: true, invitedBy: null, cancellationToken: cancellationToken);
    }

    public async Task SendInviteAsync(Guid tenantId, string tenantKey, string email, IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        if (!await tenantScope.GetRequiredService<Invites>().SendAsync(email, inviterName: "A equipe BemAqui", cancellationToken))
        {
            throw new InvalidOperationException($"Tenant {tenantKey}: o proprietário não tem convite pendente (rode a etapa CreateOwner).");
        }
    }

    public async Task<OwnerInviteLink?> IssueOwnerLinkAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        return await tenantScope.GetRequiredService<Invites>().IssueOwnerLinkAsync(cancellationToken) is { } issued
            ? new OwnerInviteLink(issued.Email, issued.Link, issued.ExpiresAt)
            : null;
    }
}
