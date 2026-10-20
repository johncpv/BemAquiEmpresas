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
    }
}
