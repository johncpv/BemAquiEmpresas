using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed partial class AuthService
{
    /// <summary>
    /// Renova a sessão (FND-04 CA 4 a 6): o refresh atual vira <c>replaced_by</c> do novo. Um refresh já
    /// substituído ainda vale dentro da tolerância (duas abas renovando juntas); fora dela é sinal de roubo e a
    /// família inteira é revogada.
    /// </summary>
    public async Task<Result<LoginSession>> RefreshAsync(string? refreshToken, string host, string? ip, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Expired();
        }

        var now = clock.GetUtcNow();
        var hash = OpaqueToken.Hash(refreshToken);
        var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (current is null || current.RevokedAt is not null || current.ExpiresAt <= now)
        {
            return Expired();
        }

        if (current.ReplacedAt is { } replacedAt && now - replacedAt > signing.Options.RefreshReuseTolerance)
        {
            await RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            await AuditAsync("RefreshReused", current.UserId, ip, new { family = current.FamilyId }, cancellationToken);
            return Expired();
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            await RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            return Expired();
        }

        var session = CreateSession(user, host, current.FamilyId, ip, now);
        var created = db.RefreshTokens.Local.Single(t => t.TokenHash == OpaqueToken.Hash(session.RefreshToken));
        current.ReplaceWith(created.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    private static Error Expired() => Error.Validation("auth.session_expired", SessionExpired);
}
