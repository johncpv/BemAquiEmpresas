using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Production.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Production.Infrastructure;

/// <summary>Calendário padrão do tenant (CAD-03 CA 8): seg a sex, sem exceções. Idempotente; não mexe no que o tenant mudou.</summary>
public sealed class CalendarSeeder : ITenantSeeder
{
    public int Order => 40;

    public async Task SeedAsync(IServiceProvider tenantScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantScope);
        await tenantScope.GetRequiredService<CalendarService>().DefaultEntityAsync(cancellationToken);
    }
}
