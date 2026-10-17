using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using Microsoft.Extensions.Logging;

namespace BemAqui.Platform.Infrastructure.Provisioning;

/// <summary>
/// Implementação provisória das etapas CreateOwner e SendInvite: o usuário proprietário e o convite por e-mail
/// chegam nas FND-04/06c e FND-08, que substituem esta classe pelo registro do Identity.
/// </summary>
public sealed partial class PendingOwnerOnboarding(ILogger<PendingOwnerOnboarding> logger) : ITenantOwnerOnboarding
{
    public Task CreateOwnerAsync(Tenant tenant, string email, IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        LogPending(logger, "criação do proprietário", tenant.Key);
        return Task.CompletedTask;
    }

    public Task SendInviteAsync(Tenant tenant, string email, IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        LogPending(logger, "envio do convite", tenant.Key);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Provisionamento do tenant {Tenant}: {Etapa} ainda não implementada (FND-04/FND-08).")]
    private static partial void LogPending(ILogger logger, string etapa, string tenant);
}
