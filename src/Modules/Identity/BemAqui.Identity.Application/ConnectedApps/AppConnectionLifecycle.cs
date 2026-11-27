using System.Globalization;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.ConnectedApps;

/// <summary>
/// Vida da conexão (INT-01 CA 12 a 14, 17, 21 e 22): revogar e suspender com os tokens, o evento, a auditoria e o aviso aos Proprietários na
/// mesma gravação; depois da gravação, o cache do token cai. Os textos são os da tela (CA 20).
/// </summary>
public sealed class AppConnectionLifecycle(
    IIdentityDb db,
    IEventPublisher events,
    INotificationService notifications,
    IUserDirectory users,
    AppConnectionCache cache,
    TimeProvider clock)
{
    public const string PageLink = "/app/configuracoes/integracoes/conecta";
    public const string NotificationType = "conecta";

    public const string ReuseText = "Desconectada por segurança: o Conecta usou uma credencial antiga.";
    public const string ByAppText = "Desconectada pelo Conecta";
    public const string ModuleOffText = "Desconectada porque o módulo Conecta ficou desligado por mais de 30 dias.";

    public static string ByUserText(string? name, DateTimeOffset at) => "Desconectada por " + (name ?? "um Proprietário") + " em " + Day(at);

    public static string ReplacedText(string? name, DateTimeOffset at) => "Substituída por nova conexão em " + Day(at) + " por " + (name ?? "um Proprietário");

    public static string Day(DateTimeOffset at) => BusinessTime.ToBusiness(at).ToString("dd/MM", CultureInfo.InvariantCulture);

    /// <summary>Revoga a conexão e todos os tokens dela (sem gravar: quem chama grava na mesma transação e depois chama <see cref="AfterSave"/>).</summary>
    public async Task<bool> RevokeAsync(AppConnection connection, AppConnectionReason reason, string text, Guid? by, bool notifyOwners, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var now = clock.GetUtcNow();
        if (!connection.Revoke(reason, text, by, now))
        {
            return false;
        }

        await db.AppTokens.Where(t => t.ConnectionId == connection.Id && t.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
        events.Publish(new AppConnectionRevoked(connection.Id, connection.AppId));
        Audit(connection.Id, "AppConnectionRevoked", by, new { reason = reason.ToString(), text });
        if (notifyOwners)
        {
            await NotifyOwnersAsync("Conecta: " + text, cancellationToken);
        }

        return true;
    }

    public async Task<bool> SuspendAsync(AppConnection connection, AppConnectionReason reason, string ownerText, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (!connection.Suspend(reason, clock.GetUtcNow()))
        {
            return false;
        }

        events.Publish(new AppConnectionSuspended(connection.Id, connection.AppId));
        Audit(connection.Id, "AppConnectionSuspended", null, new { reason = reason.ToString() });
        await NotifyOwnersAsync(ownerText, cancellationToken);
        return true;
    }

    public void AfterSave(AppConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cache.Invalidate(connection.Id);
    }

    /// <summary>Autorizador desativado ou desligado (USR-01): as conexões dele ficam suspensas e os Proprietários recebem "Autorizar de novo" (CA 17).</summary>
    public async Task SuspendForGrantorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var active = await db.AppConnections.Where(c => c.GrantorUserId == userId && c.Status == AppConnectionStatus.Active).ToListAsync(cancellationToken);
        if (active.Count == 0)
        {
            return;
        }

        var name = await users.DisplayNameAsync(userId, cancellationToken) ?? "quem autorizou";
        foreach (var connection in active)
        {
            await SuspendAsync(connection, AppConnectionReason.GrantorInactive,
                "Conecta suspenso: " + name + ", que autorizou a conexão, perdeu o acesso. Autorize de novo.", cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        active.ForEach(AfterSave);
    }

    /// <summary>2FA do autorizador zerado: não suspende, só avisa os Proprietários com "Ver conexão" (CA 17).</summary>
    public async Task NotifyTwoFactorResetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await db.AppConnections.AnyAsync(c => c.GrantorUserId == userId && c.Status == AppConnectionStatus.Active, cancellationToken))
        {
            return;
        }

        var name = await users.DisplayNameAsync(userId, cancellationToken) ?? "quem autorizou";
        await NotifyOwnersAsync("O 2FA de " + name + ", que autorizou o Conecta, foi zerado em " + Day(clock.GetUtcNow()) + ". Ver conexão.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task NotifyOwnersAsync(string title, CancellationToken cancellationToken)
    {
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, NotificationType, title, PageLink);
        }
    }

    public void Audit(Guid entityId, string action, Guid? userId, object details, string? ip = null, string entity = nameof(AppConnection)) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = entity,
            EntityId = entityId.ToString(),
            Action = action,
            UserId = userId,
            At = clock.GetUtcNow(),
            Ip = ip,
            Changes = JsonSerializer.Serialize(details),
        });
}
