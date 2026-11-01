using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Finance.Infrastructure;

/// <summary>Limites do Finance (FIN-01, D8): grava os padrões que faltam, sem sobrescrever o que o tenant mudou.</summary>
public sealed class FinanceParametersSeeder : ITenantSeeder
{
    public int Order => 42;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IFinanceDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var existing = (await db.FinanceParameters.Select(p => p.Key).ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
        foreach (var (key, value) in FinanceParametersService.Defaults.Where(d => !existing.Contains(d.Key)))
        {
            db.FinanceParameters.Add(new FinanceParameter { Key = key, Value = value, UpdatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);

        // FIN-08 (ADR 0042): contas do DRE e regra do sinal, pré-configuradas e "A confirmar com a contadora".
        await DreParametersService.SeedAsync(db, clock, cancellationToken);

        // BNK-01b (ADR 0042): Configurações → Cobrança, com multa, juros e a regra do vencido "A confirmar com a contadora".
        await BillingSettingsService.SeedAsync(db, clock, cancellationToken);

        // FIN-09 (ADR 0042): a régua de cobrança sugerida (−3, 0, +3 e +10 dias, 09:00), desligada.
        await CollectionRuleService.SeedAsync(db, clock, cancellationToken);
    }
}
