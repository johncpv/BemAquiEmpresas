namespace BemAqui.Platform.Contracts;

/// <summary>
/// Dono do tenant no provisionamento (FND-03a etapas CreateOwner e SendInvite), implementado pelo Identity.
/// Roda no escopo do tenant novo. As duas etapas são idempotentes: rodar de novo não duplica nada.
/// </summary>
public interface ITenantOwnerOnboarding
{
    Task CreateOwnerAsync(Guid tenantId, string tenantKey, string email, IServiceProvider tenantScope, CancellationToken cancellationToken);

    Task SendInviteAsync(Guid tenantId, string tenantKey, string email, IServiceProvider tenantScope, CancellationToken cancellationToken);

    /// <summary>
    /// Link do convite pendente do proprietário, sem e-mail (CLI <c>tenant invite-owner</c>): gera um token novo e o link
    /// anterior deixa de valer. Null se não há convite pendente (o proprietário já criou a senha).
    /// </summary>
    Task<OwnerInviteLink?> IssueOwnerLinkAsync(IServiceProvider tenantScope, CancellationToken cancellationToken);
}

public sealed record OwnerInviteLink(string Email, string Link, DateTimeOffset ExpiresAt);
