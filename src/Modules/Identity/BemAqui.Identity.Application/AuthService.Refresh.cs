using BemAqui.BuildingBlocks.Security;
using BemAqui.Identity.Application.Shopfloor;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed partial class AuthService
{
    /// <summary>
    /// Renova a sessão (FND-04 CA 4 a 6): o refresh atual vira <c>replaced_by</c> do novo. Um refresh já substituído ainda vale
    /// dentro da tolerância (duas abas renovando juntas); fora dela é sinal de roubo e a família inteira é revogada. A família tem
    /// prazo absoluto (ADR 0026 §3): passou, 401 e novo login. A sessão por PIN continua por PIN (<c>amr = ["pin"]</c>), presa ao
    /// aparelho do cookie, e nunca vira sessão completa. A renovação só vale no host em que a sessão nasceu.
    /// </summary>
    public async Task<Result<LoginSession>> RefreshAsync(string? refreshToken, string host, string? ip, CancellationToken cancellationToken = default) =>
        await RefreshAsync(refreshToken, host, ip, deviceSecret: null, cancellationToken);

    public async Task<Result<LoginSession>> RefreshAsync(string? refreshToken, string host, string? ip, string? deviceSecret, CancellationToken cancellationToken = default)
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

        if (current.Host is { } origin && !string.Equals(origin, NormalizeHost(host), StringComparison.Ordinal))
        {
            await AuditAsync("RefreshOtherHost", current.UserId, ip, new { family = current.FamilyId }, cancellationToken);
            return Expired();
        }

        var absolute = current.AbsoluteExpiresAt ?? await LegacyAbsoluteAsync(current.FamilyId, cancellationToken);
        if (absolute <= now)
        {
            await RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            await AuditAsync("SessionAbsoluteExpired", current.UserId, ip, new { family = current.FamilyId, kind = current.SessionKind.ToString() }, cancellationToken);
            return Expired();
        }

        if (current.SessionKind == SessionKind.Pin)
        {
            // A sessão por PIN só renova no aparelho dela, com o segredo atual do cookie (ADR 0026 §1 e §3).
            var device = await devices.ResolveAsync(deviceSecret, cancellationToken);
            if (device is null || device.Id != current.DeviceId)
            {
                await RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
                await AuditAsync("PinRefreshRejected", current.UserId, ip, new { device = current.DeviceId }, cancellationToken);
                return Expired();
            }

            device.Touch(now);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.UserId, cancellationToken);
        if (user is null || user.Status != UserStatus.Active)
        {
            await RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            return Expired();
        }

        var amr = current.SessionKind == SessionKind.Pin ? new[] { AuthMethods.Pin }
            : user.TotpEnabled ? [AuthMethods.Password, AuthMethods.Otp] : [AuthMethods.Password];
        var session = CreateSession(user, host, current.FamilyId, ip, now, new SessionSpec(current.SessionKind, current.DeviceId, absolute, amr));
        var created = db.RefreshTokens.Local.Single(t => t.TokenHash == OpaqueToken.Hash(session.RefreshToken!));
        current.ReplaceWith(created.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    /// <summary>Família aberta antes da PRD-03b: o prazo absoluto conta do início dela (CA 21).</summary>
    private async Task<DateTimeOffset> LegacyAbsoluteAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var start = await db.RefreshTokens.Where(t => t.FamilyId == familyId).MinAsync(t => t.CreatedAt, cancellationToken);
        return SessionLifetimes.FullAbsolute(start, await AbsoluteDaysAsync(cancellationToken));
    }

    private static Error Expired() => Error.Validation("auth.session_expired", SessionExpired);
}