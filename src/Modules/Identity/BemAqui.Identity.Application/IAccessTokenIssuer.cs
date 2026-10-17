using BemAqui.Identity.Domain;

namespace BemAqui.Identity.Application;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Access token JWT de 15 min só com identidade: sub, tid, aud, sstamp, iat, exp e jti (ADR 0008).</summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(User user, Guid tenantId, string audienceHost);
}
