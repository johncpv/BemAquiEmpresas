using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.Identity.Application.ConnectedApps;

/// <summary>
/// Esquema <c>App</c> (INT-01 CA 15 e 17): acha o token de acesso pelo hash, confere validade, conexão <c>Active</c> e autorizador ativo, e devolve a
/// concessão. Autorizador desativado sem a suspensão gravada (caminho que não passou pelo <c>UserLifecycle</c>) suspende aqui. O último uso e o IP
/// são gravados no máximo uma vez por minuto, fora da auditoria.
/// </summary>
public sealed class AppTokenAuthenticator(
    IIdentityDb db,
    IOptions<ConnectedAppOptions> options,
    AppConnectionCache cache,
    AppConnectionLifecycle lifecycle,
    ITenantContext tenant,
    TimeProvider clock) : IConnectedAppAuthenticator
{
    public async Task<AppTokenResult> AuthenticateAsync(string accessToken, string? ip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accessToken);
        if (!accessToken.StartsWith(AppClaims.AccessPrefix, StringComparison.Ordinal) || accessToken.Length > 128 || tenant.Tenant is not { } current)
        {
            return AppTokenResult.Invalid;
        }

        var hash = OpaqueToken.Hash(accessToken);
        if (cache.TryGet(current.Id, hash, out var cached) && cached is not null)
        {
            return cached;
        }

        var now = clock.GetUtcNow();
        var row = await (
                from t in db.AppTokens.AsNoTracking()
                join c in db.AppConnections.AsNoTracking() on t.ConnectionId equals c.Id
                join a in db.ConnectedApps.AsNoTracking() on c.AppId equals a.Id
                join u in db.Users.AsNoTracking() on c.GrantorUserId equals u.Id
                where t.TokenHash == hash && t.Kind == AppTokenKind.Access && t.RevokedAt == null && t.ExpiresAt > now
                select new { c.Id, c.AppId, a.ClientId, c.Status, c.StatusReason, c.GrantedScopes, c.GrantorUserId, GrantorName = u.Name, GrantorStatus = u.Status })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return AppTokenResult.Invalid;
        }

        if (row.Status == AppConnectionStatus.Active && row.GrantorStatus != UserStatus.Active)
        {
            await lifecycle.SuspendForGrantorAsync(row.GrantorUserId, cancellationToken);
            return new AppTokenResult(AppTokenStatus.GrantorInactive, null);
        }

        if (row.Status == AppConnectionStatus.Suspended && row.StatusReason == AppConnectionReason.GrantorInactive)
        {
            return new AppTokenResult(AppTokenStatus.GrantorInactive, null);
        }

        if (row.Status != AppConnectionStatus.Active)
        {
            return AppTokenResult.Invalid;
        }

        var limit = now - AppConnection.TouchInterval;
        await db.AppConnections.Where(c => c.Id == row.Id && (c.LastUsedAt == null || c.LastUsedAt < limit))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, now).SetProperty(c => c.LastUsedIp, ip), cancellationToken);
        var name = string.Equals(row.ClientId, options.Value.ClientId, StringComparison.Ordinal) ? options.Value.Name : row.ClientId;
        var result = new AppTokenResult(AppTokenStatus.Valid,
            new ConnectedAppGrant(row.Id, row.AppId, row.ClientId, name, row.GrantorUserId, row.GrantorName, row.GrantedScopes.ToHashSet(StringComparer.Ordinal)));
        cache.Set(current.Id, hash, row.Id, result);
        return result;
    }
}
