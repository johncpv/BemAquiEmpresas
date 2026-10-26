using System.Collections.Concurrent;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;

namespace BemAqui.Identity.Application.Kiosk;

/// <summary>
/// Avisos e eventos de segurança das telas de TV (ADR 0030 §4): notificação a todos os Proprietários ativos e entrada na auditoria
/// imutável, nunca com o token, o cookie nem o hash.
/// </summary>
public sealed class KioskNotices(IIdentityDb db, INotificationService notifications, IUserDirectory users, TenantContextHolder tenant, ICurrentUser currentUser, TimeProvider clock)
{
    public const string NotificationType = "security.kiosk";
    public const string ScreensLink = "/app/painel-tv";

    /// <summary>Nome que a TV mostra quando a tela é desligada ("Esta tela foi desligada por {empresa}.").</summary>
    public string CompanyName => tenant.Resolved is { } resolved ? resolved.TradeName ?? resolved.LegalName : "a empresa";

    public async Task NotifyOwnersAsync(string title, CancellationToken cancellationToken)
    {
        foreach (var owner in await users.OwnerIdsAsync(cancellationToken))
        {
            notifications.Notify(owner, NotificationType, title, ScreensLink);
        }
    }

    public async Task<string> WhoAsync(Guid? userId, CancellationToken cancellationToken) =>
        userId is { } id ? await users.DisplayNameAsync(id, cancellationToken) ?? "um usuário" : "o sistema";

    public void Audit(Guid screenId, string action, object? details) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Entity = nameof(Domain.KioskScreen),
            EntityId = screenId.ToString(),
            Action = action,
            UserId = currentUser.UserId,
            At = clock.GetUtcNow(),
            Ip = currentUser.IpAddress,
            Changes = details is null ? "{}" : JsonSerializer.Serialize(details),
        });
}

/// <summary>
/// Versão da validação em cache por tela (ADR 0030 §2): revogar, gerar novo link ou mudar a configuração incrementa, e a próxima
/// conferência relê o banco. Vale para uma instância da API (a VPS roda uma), como o registro de conexões do hub.
/// </summary>
public sealed class KioskSessionVersions
{
    private readonly ConcurrentDictionary<(Guid Tenant, Guid Kiosk), int> _versions = new();

    public int Of(Guid tenantId, Guid kioskId) => _versions.GetValueOrDefault((tenantId, kioskId));

    public void Bump(Guid tenantId, Guid kioskId) => _versions.AddOrUpdate((tenantId, kioskId), 1, (_, v) => v + 1);
}
