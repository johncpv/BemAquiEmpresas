using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Finance.Infrastructure;

/// <summary>
/// Condições do seed (CAD-05b CA 15): À vista, 30, 30/60 e 30/60/90 dias, pela <c>seed_key</c>. Idempotente, roda em todo
/// provisionamento e todo <c>migrate</c>, e não recria a que o tenant excluiu (busca também as excluídas).
/// </summary>
public sealed class PaymentTermSeeder : ITenantSeeder
{
    public static readonly IReadOnlyList<(string Key, string Name, Installment[] Installments)> Seeds =
    [
        ("a-vista", "À vista", [new(0, 100m)]),
        ("30-dias", "30 dias", [new(30, 100m)]),
        ("30-60-dias", "30/60 dias", [new(30, 50m), new(60, 50m)]),
        ("30-60-90-dias", "30/60/90 dias", [new(30, 33.3333m), new(60, 33.3333m), new(90, 33.3334m)]),
    ];

    public int Order => 40;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IFinanceDb>();
        var clock = tenantScope.GetRequiredService<TimeProvider>();
        var existing = (await db.PaymentTerms.IgnoreQueryFilters().Where(t => t.SeedKey != null).Select(t => t.SeedKey!).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (key, name, installments) in Seeds.Where(s => !existing.Contains(s.Key)))
        {
            db.PaymentTerms.Add(new PaymentTerm { SeedKey = key, Name = name, Installments = [.. installments], CreatedAt = clock.GetUtcNow() });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
