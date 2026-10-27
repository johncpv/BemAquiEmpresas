using BemAqui.Identity.Domain;

namespace BemAqui.Identity.Application;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Access token JWT de 15 min só com identidade: sub, tid, aud, sstamp, iat, exp e jti (ADR 0008), mais o <c>amr</c> (adendo de
/// 21/10/2026): <c>pwd</c>, <c>otp</c> ou <c>pin</c>. A sessão por PIN (<c>amr = ["pin"]</c>) só alcança a lista permitida.
/// </summary>
public interface IAccessTokenIssuer
{
    /// <param name="otpAt">Instante da confirmação do código TOTP agora (login com o código ou "2FA de novo"): vira a claim
    /// <c>otp_at</c>, a prova que o <c>RequireRecentTwoFactor</c> confere (BNK-01a CA 1). Nulo na renovação silenciosa.</param>
    AccessToken Issue(User user, Guid tenantId, string audienceHost, IReadOnlyList<string> amr, DateTimeOffset? otpAt = null);
}
