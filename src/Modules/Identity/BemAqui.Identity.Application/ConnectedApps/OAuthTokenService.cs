using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Observability;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BemAqui.Identity.Application.ConnectedApps;

public sealed record OAuthOutcome(OAuthTokenResponse? Response, OAuthError? Error)
{
    public static implicit operator OAuthOutcome(OAuthTokenResponse response) => new(response, null);

    public static implicit operator OAuthOutcome(OAuthError error) => new(null, error);
}

/// <summary>
/// <c>/oauth/token</c> e <c>/oauth/revoke</c> (INT-01 CA 11 a 13 e 22, ADR 0053 §2 e §3): cliente confidencial (<c>client_secret_basic</c>, segredo do
/// ambiente comparado em tempo constante), código de uso único com PKCE S256, renovação rotativa com detecção de reuso e revogação pelo aplicativo.
/// Nada do código, do verificador, do segredo ou dos tokens vai para o log ou para a auditoria.
/// </summary>
public sealed class OAuthTokenService(
    IIdentityDb db,
    IOptions<ConnectedAppOptions> options,
    OAuthAuthorizeService authorize,
    AppConnectionLifecycle lifecycle,
    IOutboundSubscriptions subscriptions,
    IEventPublisher events,
    IAlertSink alerts,
    IMemoryCache memory,
    ITenantContext tenant,
    AuditOrigin origin,
    TimeProvider clock)
{
    public const int AuthFailureThreshold = 20;
    public static readonly TimeSpan AuthFailureWindow = TimeSpan.FromMinutes(5);

    private static OAuthError InvalidGrant(string description) => new(OAuthError.InvalidGrant, description);

    private static readonly OAuthError InvalidClient = new(OAuthError.InvalidClient, "Autenticação do cliente recusada.");

    private ConnectedAppOptions App => options.Value;

    /// <summary>Autentica o cliente pelo <c>Authorization: Basic</c>; 20 falhas em 5 min no tenant geram o alerta <c>integration_auth_failures</c> (CA 19).</summary>
    public async Task<ConnectedApp?> AuthenticateClientAsync(string? authorization, string? formClientId, CancellationToken cancellationToken)
    {
        var basic = OAuthRules.ParseBasic(authorization);
        if (basic is { } credentials
            && string.Equals(credentials.ClientId, App.ClientId, StringComparison.Ordinal)
            && (formClientId is null || string.Equals(formClientId, App.ClientId, StringComparison.Ordinal))
            && OAuthRules.SecretMatches(credentials.Secret, App.ClientSecretHash))
        {
            origin.Label = "Integração " + App.Name;
            return await authorize.EnsureAppAsync(cancellationToken);
        }

        await CountFailureAsync(cancellationToken);
        return null;
    }

    private sealed class FailureWindow
    {
        public int Count;
    }

    private async Task CountFailureAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenant.Tenant?.Id ?? Guid.Empty;
        var window = memory.GetOrCreate("oauth-client-fail:" + tenantId, e =>
        {
            e.AbsoluteExpirationRelativeToNow = AuthFailureWindow;
            return new FailureWindow();
        })!;
        if (Interlocked.Increment(ref window.Count) == AuthFailureThreshold)
        {
            await alerts.RaiseAsync(new Alert(AlertKinds.IntegrationAuthFailures, tenant.Tenant?.Key,
                AuthFailureThreshold + " falhas de autenticação de aplicativo conectado em 5 minutos."), cancellationToken);
        }
    }

    /// <summary>Troca do código (CA 11 e 12). Reapresentar o código revoga a conexão que ele criou.</summary>
    public async Task<OAuthOutcome> ExchangeCodeAsync(ConnectedApp app, string? code, string? redirectUri, string? verifier, string? ip, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrEmpty(code))
        {
            return new OAuthError(OAuthError.InvalidRequest, "Código ausente.");
        }

        var now = clock.GetUtcNow();
        var hash = OpaqueToken.Hash(code);
        var grant = await db.AppAuthorizationCodes.FirstOrDefaultAsync(c => c.CodeHash == hash && c.AppId == app.Id, cancellationToken);
        if (grant is null)
        {
            return InvalidGrant("Código inválido.");
        }

        if (grant.UsedAt is not null)
        {
            if (grant.ConnectionId is { } issued && await db.AppConnections.FirstOrDefaultAsync(c => c.Id == issued, cancellationToken) is { } reused
                && await lifecycle.RevokeAsync(reused, AppConnectionReason.CredentialReuse, AppConnectionLifecycle.ReuseText, null, notifyOwners: true, cancellationToken))
            {
                await db.SaveChangesAsync(cancellationToken);
                lifecycle.AfterSave(reused);
            }

            return InvalidGrant("Código já usado.");
        }

        if (grant.ExpiresAt <= now || !string.Equals(grant.RedirectUri, redirectUri, StringComparison.Ordinal) || !OAuthRules.VerifyPkce(verifier, grant.CodeChallenge))
        {
            return InvalidGrant("Código vencido, endereço de retorno diferente ou verificador PKCE inválido.");
        }

        var grantor = await db.Users.AsNoTracking().Where(u => u.Id == grant.UserId).Select(u => new { u.Status, u.Name }).FirstOrDefaultAsync(cancellationToken);
        if (grantor is null || grantor.Status != UserStatus.Active)
        {
            return InvalidGrant("Quem autorizou não tem mais acesso.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var redeemed = await db.AppAuthorizationCodes.Where(c => c.Id == grant.Id && c.UsedAt == null).ExecuteUpdateAsync(s => s.SetProperty(c => c.UsedAt, now), cancellationToken);
        if (redeemed == 0)
        {
            return InvalidGrant("Código já usado.");
        }

        // CA 12: no máximo uma conexão não revogada por aplicativo. A anterior sai antes do índice único receber a nova; tudo na mesma transação.
        var previous = await db.AppConnections.FirstOrDefaultAsync(c => c.AppId == app.Id && c.Status != AppConnectionStatus.Revoked, cancellationToken);
        if (previous is not null)
        {
            await lifecycle.RevokeAsync(previous, AppConnectionReason.Replaced, AppConnectionLifecycle.ReplacedText(grantor.Name, now), grant.UserId, notifyOwners: false, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        var connection = AppConnection.Create(app.Id, grant.UserId, grant.Scopes, grant.AccountLabel, now);
        db.AppConnections.Add(connection);
        grant.Redeem(connection.Id, now);
        var (access, refresh) = Issue(connection.Id, Guid.CreateVersion7(), now);
        await db.SaveChangesAsync(cancellationToken);
        var secret = await subscriptions.CreateForConnectionAsync(connection.Id, app.ClientId, previous?.Id, grant.Scopes, cancellationToken);
        events.Publish(new AppConnected(connection.Id, app.Id, app.ClientId, grant.UserId, previous?.Id));
        lifecycle.Audit(connection.Id, "AppConnected", grant.UserId, new { app = app.ClientId, scopes = grant.Scopes, replaced = previous?.Id }, ip);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (previous is not null)
        {
            lifecycle.AfterSave(previous);
        }

        return Response(access, refresh, connection, secret);
    }

    /// <summary>Renovação rotativa (CA 13): par novo na mesma família; o substituído reapresentado depois de 10 s revoga a conexão e alerta.</summary>
    public async Task<OAuthOutcome> RefreshAsync(ConnectedApp app, string? refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (string.IsNullOrEmpty(refreshToken) || !refreshToken.StartsWith(AppClaims.RefreshPrefix, StringComparison.Ordinal))
        {
            return InvalidGrant("Token de renovação inválido.");
        }

        var now = clock.GetUtcNow();
        var hash = OpaqueToken.Hash(refreshToken);
        var current = await db.AppTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Kind == AppTokenKind.Refresh, cancellationToken);
        var connection = current is null ? null : await db.AppConnections.FirstOrDefaultAsync(c => c.Id == current.ConnectionId && c.AppId == app.Id, cancellationToken);
        if (current is null || connection is null || current.RevokedAt is not null || current.ExpiresAt <= now)
        {
            return InvalidGrant("Token de renovação inválido ou vencido.");
        }

        if (current.ReplacedAt is { } replacedAt && now - replacedAt > AppToken.ReuseTolerance)
        {
            if (await lifecycle.RevokeAsync(connection, AppConnectionReason.CredentialReuse, AppConnectionLifecycle.ReuseText, null, notifyOwners: true, cancellationToken))
            {
                await db.SaveChangesAsync(cancellationToken);
                lifecycle.AfterSave(connection);
                await alerts.RaiseAsync(new Alert(AlertKinds.IntegrationTokenReuse, tenant.Tenant?.Key,
                    "Aplicativo conectado reapresentou um token de renovação substituído; a conexão foi revogada."), cancellationToken);
            }

            return InvalidGrant("Token de renovação já substituído.");
        }

        if (connection.Status != AppConnectionStatus.Active)
        {
            return InvalidGrant("A conexão não está ativa.");
        }

        if (!await db.Users.AnyAsync(u => u.Id == connection.GrantorUserId && u.Status == UserStatus.Active, cancellationToken))
        {
            await lifecycle.SuspendForGrantorAsync(connection.GrantorUserId, cancellationToken);
            return InvalidGrant("Quem autorizou não tem mais acesso.");
        }

        var (access, refresh) = Issue(connection.Id, current.FamilyId, now);
        current.ReplaceWith(db.AppTokens.Local.Single(t => t.TokenHash == OpaqueToken.Hash(refresh)).Id, now);
        await db.SaveChangesAsync(cancellationToken);
        var secret = await subscriptions.TakePendingSecretAsync(connection.Id, cancellationToken);
        return Response(access, refresh, connection, secret);
    }

    /// <summary>RFC 7009 (CA 22): o aplicativo desconecta do lado dele. Token desconhecido também responde 200.</summary>
    public async Task RevokeAsync(ConnectedApp app, string? token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (!AppClaims.IsAppToken(token))
        {
            return;
        }

        var hash = OpaqueToken.Hash(token!);
        var connectionId = await db.AppTokens.Where(t => t.TokenHash == hash).Select(t => (Guid?)t.ConnectionId).FirstOrDefaultAsync(cancellationToken);
        var connection = connectionId is null ? null : await db.AppConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.AppId == app.Id, cancellationToken);
        if (connection is not null && await lifecycle.RevokeAsync(connection, AppConnectionReason.DisconnectedByApp, AppConnectionLifecycle.ByAppText, null, notifyOwners: true, cancellationToken))
        {
            await db.SaveChangesAsync(cancellationToken);
            lifecycle.AfterSave(connection);
        }
    }

    private (string Access, string Refresh) Issue(Guid connectionId, Guid familyId, DateTimeOffset now)
    {
        var access = AppClaims.AccessPrefix + OpaqueToken.New();
        var refresh = AppClaims.RefreshPrefix + OpaqueToken.New();
        db.AppTokens.Add(new AppToken
        {
            TokenHash = OpaqueToken.Hash(access), Kind = AppTokenKind.Access, ConnectionId = connectionId, FamilyId = familyId, CreatedAt = now, ExpiresAt = now + AppToken.AccessLifetime,
        });
        db.AppTokens.Add(new AppToken
        {
            TokenHash = OpaqueToken.Hash(refresh), Kind = AppTokenKind.Refresh, ConnectionId = connectionId, FamilyId = familyId, CreatedAt = now, ExpiresAt = now + AppToken.RefreshIdleLifetime,
        });
        return (access, refresh);
    }

    private static OAuthTokenResponse Response(string access, string refresh, AppConnection connection, string? secret) =>
        new(access, "Bearer", (int)AppToken.AccessLifetime.TotalSeconds, refresh, string.Join(' ', connection.GrantedScopes), connection.Id, secret);
}
