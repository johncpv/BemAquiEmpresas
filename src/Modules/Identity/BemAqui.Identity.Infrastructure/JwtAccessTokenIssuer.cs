using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BemAqui.Identity.Infrastructure;

/// <summary>JWT HS256 de 15 min só com identidade (FND-04 CA 2): sub, tid, aud, sstamp, iat, exp, jti e amr (ADR 0008, adendo).</summary>
public sealed class JwtAccessTokenIssuer(TokenSigning signing, TimeProvider clock) : IAccessTokenIssuer
{
    public AccessToken Issue(User user, Guid tenantId, string audienceHost, IReadOnlyList<string> amr)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(amr);
        var now = clock.GetUtcNow();
        var expires = now + signing.Options.AccessTokenLifetime;
        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = signing.Options.Issuer,
            Audience = audienceHost,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id.ToString(),
                ["tid"] = tenantId.ToString(),
                ["sstamp"] = user.SecurityStamp,
                ["jti"] = Guid.CreateVersion7().ToString(),
                [AuthMethods.Claim] = amr.ToArray(),
            },
            SigningCredentials = new SigningCredentials(signing.Key, SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, expires);
    }
}
