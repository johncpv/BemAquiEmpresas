using BemAqui.Fiscal.Contracts;
using BemAqui.Fiscal.Domain;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Fiscal.Application.Settings;

/// <summary>Implementação do <see cref="IFiscalSetupStatus"/> (PERF-04): o regime em uso tem conferência com o hash do valor atual.</summary>
public sealed class FiscalSetupStatus(IFiscalDb db) : IFiscalSetupStatus
{
    public async Task<bool> RegimeConfirmedAsync(CancellationToken cancellationToken = default)
    {
        var settings = await db.FiscalSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            return false;
        }

        var key = FiscalSettingKeys.Find("regime")!;
        var hash = key.HashOf(settings);
        return await db.SettingConfirmations.AsNoTracking().AnyAsync(c => c.SettingKey == key.Key && c.ValueHash == hash, cancellationToken);
    }
}
