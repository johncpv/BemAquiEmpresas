using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BemAqui.Identity.Application.ConnectedApps;

/// <summary>
/// Telas Configurações → Integrações → Conecta e Aplicativos conectados (INT-01 CA 20 e 21), a desconexão pelo ERP e a própria conexão vista pelo
/// aplicativo. Desconectar exige <c>settings.edit</c> e não pede 2FA de novo (reduz acesso).
/// </summary>
public sealed class AppConnectService(
    IIdentityDb db,
    IOptions<ConnectedAppOptions> options,
    OAuthAuthorizeService authorize,
    AppConnectionLifecycle lifecycle,
    IUserDirectory users,
    IOutboundSubscriptions subscriptions,
    IPermissionService permissions,
    TimeProvider clock)
{
    private ConnectedAppOptions App => options.Value;

    public async Task<ConnectedAppPage> PageAsync(CancellationToken cancellationToken = default)
    {
        var app = await authorize.EnsureAppAsync(cancellationToken);
        var configured = !string.IsNullOrWhiteSpace(App.StartUrl) && App.RedirectUris.Length > 0 && !string.IsNullOrWhiteSpace(App.ClientSecretHash);
        return new ConnectedAppPage(app.ClientId, App.Name, configured, App.EffectiveScopes.Select(View).ToList(), await LatestAsync(app.Id, cancellationToken));
    }

    public async Task<IReadOnlyList<ConnectedAppListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var app = await authorize.EnsureAppAsync(cancellationToken);
        var latest = await LatestAsync(app.Id, cancellationToken);
        return latest is null ? [] : [new ConnectedAppListItem(app.ClientId, App.Name, latest)];
    }

    /// <summary>"Desconectar" (CA 21): conexão e tokens revogados na mesma gravação, cache invalidado, auditado e notificado.</summary>
    public async Task<Result> RevokeAsync(Guid connectionId, Guid userId, CancellationToken cancellationToken = default)
    {
        var connection = await db.AppConnections.FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
        if (connection is null)
        {
            return Error.NotFound(Error.Codes.NotFound, "Conexão não encontrada.");
        }

        if (connection.Status == AppConnectionStatus.Revoked)
        {
            return Error.Conflict("app_connection_revoked", "Esta conexão já está desconectada.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var name = await users.DisplayNameAsync(userId, cancellationToken);
        await lifecycle.RevokeAsync(connection, AppConnectionReason.DisconnectedByUser, AppConnectionLifecycle.ByUserText(name, clock.GetUtcNow()), userId, notifyOwners: true, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        lifecycle.AfterSave(connection);
        return Result.Success();
    }

    public async Task<IntegrationConnectionView?> SelfAsync(Guid connectionId, CancellationToken cancellationToken = default)
    {
        var connection = await db.AppConnections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken);
        return connection is null
            ? null
            : new IntegrationConnectionView(connection.Id, App.ClientId, connection.Status.ToString(), connection.GrantedScopes, connection.AccountLabel, connection.ConnectedAt, connection.ReconsentDueAt);
    }

    private async Task<AppConnectionView?> LatestAsync(Guid appId, CancellationToken cancellationToken)
    {
        var connection = await db.AppConnections.AsNoTracking().Where(c => c.AppId == appId)
            .OrderBy(c => c.Status == AppConnectionStatus.Revoked ? 1 : 0).ThenByDescending(c => c.ConnectedAt).FirstOrDefaultAsync(cancellationToken);
        if (connection is null)
        {
            return null;
        }

        var name = await users.DisplayNameAsync(connection.GrantorUserId, cancellationToken);
        var (status, reason) = Texts(connection, name);
        var canGain = connection.Status == AppConnectionStatus.Active ? await CanGainAsync(connection, cancellationToken) : [];
        return new AppConnectionView(connection.Id, connection.Status.ToString(), connection.StatusReason.ToString(), status, reason, connection.AccountLabel,
            connection.GrantorUserId, name, connection.ConnectedAt, connection.ReconsentDueAt, connection.LastUsedAt, connection.RevokedAt,
            connection.GrantedScopes.Select(View).ToList(), canGain);
    }

    /// <summary>
    /// "O Conecta pode ganhar" (INT-04 CA 8): escopos que hoje seriam concedidos (o aplicativo pode pedir e quem autorizou tem) e a concessão não tem, e
    /// tipos de aviso que a concessão permite e a inscrição não tem. "Autorizar de novo" cria a conexão nova com os escopos e a inscrição com os tipos, numa vez só.
    /// </summary>
    private async Task<IReadOnlyList<AppScopeView>> CanGainAsync(AppConnection connection, CancellationToken cancellationToken)
    {
        var granted = connection.GrantedScopes.ToHashSet(StringComparer.Ordinal);
        var held = await permissions.GetAsync(connection.GrantorUserId, cancellationToken);
        var scopes = ConnectedAppScopes.Compute(App.EffectiveScopes.Where(s => !granted.Contains(s)), App.EffectiveScopes.ToList(), held.Has).Granted.Select(View).ToList();
        var events = (await subscriptions.MissingEventsAsync(connection.Id, granted, cancellationToken))
            .Select(t => new AppScopeView(t, "Receber o aviso “" + (BuildingBlocks.Webhooks.OutboundWebhookCatalog.Find(t)?.Label ?? t) + "”"));
        return [.. scopes, .. events];
    }

    /// <summary>Chip e motivo em linguagem simples (CA 20).</summary>
    public static (string Status, string Reason) Texts(AppConnection connection, string? grantorName)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.Status switch
        {
            AppConnectionStatus.Active => ("Conectado", "Conectado em " + AppConnectionLifecycle.Day(connection.ConnectedAt) + " por " + (grantorName ?? "um Proprietário")
                + (connection.AccountLabel is { } account ? " · conta " + account : string.Empty)),
            AppConnectionStatus.Suspended => ("Suspensa", connection.StatusReason == AppConnectionReason.GrantorInactive
                ? "Quem autorizou perdeu o acesso"
                : "Autorização vencida: autorize de novo para continuar."),
            _ when connection.StatusReason == AppConnectionReason.CredentialReuse => ("Desconectada por segurança", "O Conecta usou uma credencial antiga. Conecte de novo."),
            _ => ("Desconectada", connection.RevokedReason ?? "Desconectada"),
        };
    }

    private static AppScopeView View(string scope) => new(scope, ConnectedAppScopes.Text(scope));
}
