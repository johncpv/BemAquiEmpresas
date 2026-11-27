using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Integration;
using BemAqui.BuildingBlocks.Messaging;
using BemAqui.BuildingBlocks.Webhooks;
using BemAqui.Identity.Contracts;
using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application.ConnectedApps;

/// <summary>
/// Reavaliação da conexão antes de cada aviso (INT-02 CA 8, ADR 0053 §6): conexão <c>Active</c>, autorizador ativo e o escopo na concessão ∩
/// permissões atuais do autorizador (que já se limitam aos módulos liberados). Perda de permissão vale no envio seguinte.
/// </summary>
public sealed class ConnectedAppAccess(IIdentityDb db, IPermissionService permissions) : IConnectedAppAccess
{
    public const string InactiveReason = "Conexão não está ativa";
    public const string GrantorReason = "Quem autorizou não tem mais acesso";

    public async Task<string?> DenyReasonAsync(Guid connectionId, string? requiredScope, CancellationToken cancellationToken = default)
    {
        var connection = await db.AppConnections.AsNoTracking().Where(c => c.Id == connectionId)
            .Select(c => new { c.Status, c.GrantedScopes, c.GrantorUserId }).FirstOrDefaultAsync(cancellationToken);
        if (connection is null || connection.Status != AppConnectionStatus.Active)
        {
            return InactiveReason;
        }

        if (!await db.Users.AnyAsync(u => u.Id == connection.GrantorUserId && u.Status == UserStatus.Active, cancellationToken))
        {
            return GrantorReason;
        }

        if (requiredScope is null)
        {
            return null;
        }

        return connection.GrantedScopes.Contains(requiredScope, StringComparer.Ordinal)
            && (await permissions.GetAsync(connection.GrantorUserId, cancellationToken)).Has(requiredScope)
            ? null
            : OutboundWebhookCatalog.MissingScopeReason(requiredScope);
    }

    public async Task<IReadOnlyList<string>> GrantedScopesAsync(Guid connectionId, CancellationToken cancellationToken = default) =>
        await db.AppConnections.AsNoTracking().Where(c => c.Id == connectionId).Select(c => c.GrantedScopes).FirstOrDefaultAsync(cancellationToken) ?? [];
}

/// <summary>
/// Consumidores da vida da conexão (INT-02 CA 3): só encerram ou suspendem a inscrição de avisos, na transação do consumidor (outbox + inbox).
/// </summary>
public sealed class OutboundSubscriptionConsumers(IOutboundSubscriptions subscriptions)
    : IIntegrationEventConsumer<AppConnectionRevoked>, IIntegrationEventConsumer<AppConnectionSuspended>
{
    public Task HandleAsync(AppConnectionRevoked integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return subscriptions.EndForConnectionAsync(integrationEvent.ConnectionId, cancellationToken);
    }

    public Task HandleAsync(AppConnectionSuspended integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        return subscriptions.HoldForConnectionAsync(integrationEvent.ConnectionId, cancellationToken);
    }
}
