using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Fiscal.Infrastructure;

/// <summary>
/// Seed <c>fiscal-defaults</c> (FIS-02a, ADR 0016 e 0042): grava a configuração fiscal com os valores propostos no tenant novo e nos
/// existentes no próximo deploy, só se a linha não existir; nunca sobrescreve valor salvo. Tudo nasce "A confirmar com a contadora".
/// </summary>
public sealed class FiscalSettingsSeeder : ITenantSeeder
{
    public int Order => 60;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IFiscalDb>();
        if (await db.FiscalSettings.AnyAsync(cancellationToken))
        {
            return;
        }

        db.FiscalSettings.Add(FiscalSettingsService.NewDefaults(tenantScope.GetRequiredService<TimeProvider>().GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}
