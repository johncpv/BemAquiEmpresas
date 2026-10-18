using BemAqui.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Identity.Application;

public sealed record SecuritySettingsDto(bool RequireTwoFactor);

/// <summary>
/// Configurações → Segurança (FND-06c CA 13): "Exigir verificação em duas etapas para todos" grava
/// <c>security.require_2fa</c> (auditado pelo interceptor) e vale no próximo login de cada usuário (regra da FND-05).
/// </summary>
public sealed class SecuritySettings(IIdentityDb db, TimeProvider clock)
{
    public async Task<SecuritySettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var value = await db.TenantSettings.Where(s => s.Key == TenantSetting.RequireTwoFactor).Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
        return new SecuritySettingsDto(bool.TryParse(value, out var required) && required);
    }

    public async Task<SecuritySettingsDto> SaveAsync(SecuritySettingsDto settings, Guid actorId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var row = await db.TenantSettings.FirstOrDefaultAsync(s => s.Key == TenantSetting.RequireTwoFactor, cancellationToken);
        if (row is null)
        {
            row = new TenantSetting { Key = TenantSetting.RequireTwoFactor };
            db.TenantSettings.Add(row);
        }

        row.Value = settings.RequireTwoFactor ? "true" : "false";
        row.UpdatedAt = clock.GetUtcNow();
        row.UpdatedBy = actorId;
        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }
}
