using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;

using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Security;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.Shopfloor;

/// <summary>
/// Aparelho do cookie <c>__Host-bq_dev</c> (ADR 0026 §1): acha pelo hash do segredo atual ou do anterior. O anterior só vale dentro da
/// tolerância da rotação (a mesma do refresh); fora dela é reuso, e o aparelho é revogado na hora, como a família de refresh.
/// </summary>
public sealed class ShopfloorDeviceGate(IIdentityDb db, TokenSigning signing, ShopfloorRevocations revocations, TimeProvider clock)
{
    /// <summary>O aparelho ativo do segredo (rastreado, para rotação e uso), ou null: sem cookie, desconhecido, revogado ou vencido.</summary>
    public async Task<ShopfloorDevice?> ResolveAsync(string? secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 128)
        {
            return null;
        }

        var hash = OpaqueToken.Hash(secret);
        var device = await db.ShopfloorDevices.FirstOrDefaultAsync(d => d.SecretHash == hash || d.PreviousSecretHash == hash, cancellationToken);
        var now = clock.GetUtcNow();
        if (device is null || !device.IsActive(now))
        {
            return null;
        }

        switch (device.Match(hash, now, signing.Options.RefreshReuseTolerance))
        {
            case DeviceSecretMatch.Current:
            case DeviceSecretMatch.PreviousInTolerance:
                return device;
            case DeviceSecretMatch.Reused:
                await revocations.RevokeDeviceAsync(device, null, "secret_reused", notifyOwners: true, cancellationToken);
                return null;
            default:
                return null;
        }
    }
}

/// <summary>
/// Revogações do chão de fábrica: aparelho (PIN deixa de funcionar e as sessões por PIN dele caem, CA 8 e 9) e PIN do usuário em todos
/// os aparelhos (troca do vínculo com o funcionário, CA 19). Grava auditoria e, quando pedido, avisa o Proprietário.
/// </summary>
public sealed class ShopfloorRevocations(
    IIdentityDb db,
    INotificationService notifications,
    IUserDirectory users,
    IRealtimeConnections connections,
    ITenantContext tenant,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public const string NotificationType = "security.shopfloor_device";
    public const string DevicesLink = "/app/configuracoes/seguranca";

    public async Task RevokeDeviceAsync(ShopfloorDevice device, Guid? by, string reason, bool notifyOwners, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        var now = clock.GetUtcNow();
        device.Revoke(by, reason, now);
        var sessions = await db.RefreshTokens.Where(t => t.DeviceId == device.Id && t.RevokedAt == null).ToListAsync(cancellationToken);
        foreach (var token in sessions)
        {
            token.Revoke(now);
        }

        var pins = await db.UserPins.Where(p => p.DeviceId == device.Id).ToListAsync(cancellationToken);
        db.UserPins.RemoveRange(pins);
        Audit("ShopfloorDevice", device.Id.ToString(), "DeviceRevoked", by, new { device = device.Id, reason, sessions = sessions.Count, pins = pins.Count });
        if (notifyOwners)
        {
            var who = by is { } actor ? await users.DisplayNameAsync(actor, cancellationToken) ?? "um usuário" : "o sistema";
            var title = reason == "secret_reused"
                ? $"Aparelho {device.Name} revogado: o segredo antigo do aparelho foi reapresentado."
                : $"Aparelho {device.Name} revogado por {who}.";
            await NotifyOwnersAsync(title, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        CloseConnections(sessions.Select(s => s.UserId).Distinct().ToList());
    }

    /// <summary>Apaga o PIN do usuário em todos os aparelhos e revoga as sessões por PIN dele (CA 19).</summary>
    public async Task<int> EraseUserPinsAsync(Guid userId, string reason, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var pins = await db.UserPins.Where(p => p.UserId == userId).ToListAsync(cancellationToken);
        var sessions = await db.RefreshTokens.Where(t => t.UserId == userId && t.SessionKind == SessionKind.Pin && t.RevokedAt == null).ToListAsync(cancellationToken);
        if (pins.Count == 0 && sessions.Count == 0)
        {
            return 0;
        }

        db.UserPins.RemoveRange(pins);
        foreach (var token in sessions)
        {
            token.Revoke(now);
        }

        Audit("Auth", userId.ToString(), "PinErased", userId, new { reason, pins = pins.Count, sessions = sessions.Count });
        await db.SaveChangesAsync(cancellationToken);
        CloseConnections([userId]);
        return pins.Count;
    }

    public async Task NotifyOwnersAsync(string title, CancellationToken cancellationToken)
    {
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, NotificationType, title, DevicesLink);
        }
    }

    /// <summary>Evento de segurança na auditoria imutável: quem, quando, qual aparelho e o IP; nunca o PIN nem o segredo (CA 23).</summary>
    public void Audit(string entity, string entityId, string action, Guid? userId, object? details) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = entity,
            EntityId = entityId,
            Action = action,
            UserId = userId ?? currentUser.UserId,
            At = clock.GetUtcNow(),
            Ip = currentUser.IpAddress,
            Changes = details is null ? "{}" : JsonSerializer.Serialize(details),
        });

    private void CloseConnections(List<Guid> userIds)
    {
        if (userIds.Count == 0 || tenant.Tenant?.Id is not { } tenantId)
        {
            return;
        }

        connections.CloseFor(tenantId, userIds); // já gravado: a reconexão do hub passa de novo pela autorização
    }
}