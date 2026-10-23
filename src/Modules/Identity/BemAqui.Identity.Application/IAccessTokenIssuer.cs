using BemAqui.Identity.Domain;

namespace BemAqui.Identity.Application;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Access token JWT de 15 min só com identidade: sub, tid, aud, sstamp, iat, exp e jti (ADR 0008), mais o <c>amr</c> (adendo de
/// 21/10/2026): <c>pwd</c>, <c>otp</c> ou <c>pin</c>. A sessão por PIN (<c>amr = ["pin"]</c>) só alcança a lista permitida.
/// </summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(User user, Guid tenantId, string audienceHost, IReadOnlyList<string> amr);
}
