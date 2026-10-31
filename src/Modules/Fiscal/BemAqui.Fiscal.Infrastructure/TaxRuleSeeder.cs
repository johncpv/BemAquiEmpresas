using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Fiscal.Application;
using BemAqui.Fiscal.Application.Settings;
using BemAqui.Fiscal.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Fiscal.Infrastructure;

/// <summary>
/// Seed <c>fiscal-tax-rules</c> (FIS-02b CA 1, ADR 0016 e 0042): no tenant novo e nos existentes no próximo deploy, uma vez e só se o tenant
/// não tiver nenhuma regra, grava as regras montadas a partir dos valores de Configurações → Fiscal (roda depois do <c>fiscal-defaults</c>),
/// todas em rascunho, "A confirmar com a contadora" e com o sistema como autor.
/// </summary>
public sealed class TaxRuleSeeder : ITenantSeeder
{
    public int Order => 61;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IFiscalDb>();
        if (await db.TaxRules.AnyAsync(cancellationToken))
        {
            return;
        }

        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var settings = await db.FiscalSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? FiscalSettingsService.NewDefaults(clock.GetUtcNow());
        db.TaxRules.AddRange(TaxRuleSeeds.Build(settings, BusinessTime.Today(clock), clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}
