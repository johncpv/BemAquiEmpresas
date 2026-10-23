using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Inventory.Infrastructure;

/// <summary>
/// Parâmetros do estoque (EST-01a, D7): R$ 1.000,00 de limite e 20% de desvio de custo para os lançamentos manuais, com a janela do
/// saldo inicial aberta. Idempotente: roda em todo provisionamento e todo <c>migrate</c> e nunca sobrescreve o que já existe.
/// </summary>
public sealed class InventoryParametersSeeder : ITenantSeeder
{
    public int Order => 41;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        var db = tenantScope.GetRequiredService<IInventoryDb>();
        if (await db.InventoryParameters.AnyAsync(cancellationToken))
        {
            return;
        }

        db.InventoryParameters.Add(new InventoryParameters());
        await db.SaveChangesAsync(cancellationToken);
    }
}
