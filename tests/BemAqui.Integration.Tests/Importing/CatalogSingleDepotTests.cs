using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Migrations.Application;
using BemAqui.Migrations.Domain;
using BemAqui.Platform.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Importing;

/// <summary>MIG-01c CA 11: um único depósito ativo é ligado ao ALM, sem criar outro local (tenant próprio da classe).</summary>
public class CatalogSingleDepotTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    [Fact]
    public async Task Deposito_unico_liga_ao_ALM()
    {
        var source = new FakeSnapshot();
        source.Table("llx_entrepot").Add(new() { ["rowid"] = 1, ["ref"] = "Principal", ["label"] = "Depósito principal", ["statut"] = 1, ["fk_parent"] = 0, ["entity"] = 1 });
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        var sp = scope.ServiceProvider;
        var runner = new ImportRunner(sp.GetRequiredService<IMigrationDb>(), sp.GetServices<IDolibarrLoader>(), sp.GetRequiredService<ITenantProfile>(),
            sp.GetRequiredService<ITenantContext>(), sp.GetRequiredService<AuditOrigin>(), TimeProvider.System);
        var result = await runner.RunAsync(source, new ImportRequest(ImportMode.Real, ["locations"], "operador@konebras.com.br", ImportMapping.Empty));

        result.Report!.Entities.Single().Should().BeEquivalentTo(new { Read = 1, Linked = 1 });
        var locations = await sp.GetRequiredService<IInventoryDb>().Locations.AsNoTracking().ToListAsync();
        locations.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Code = "ALM", IsDefault = true, ImportSource = "dolibarr:llx_entrepot:1" });
    }
}
