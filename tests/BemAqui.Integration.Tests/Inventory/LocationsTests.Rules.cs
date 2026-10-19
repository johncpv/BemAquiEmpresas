using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

public partial class LocationsTests
{
    [Fact]
    public async Task Seed_cria_o_ALM_padrao_uma_vez()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
        var alm = await db.Locations.IgnoreQueryFilters().SingleAsync(l => l.SeedKey == LocationSeeder.SeedKey);
        alm.Code.Should().Be("ALM");
        alm.Name.Should().Be("Almoxarifado principal");

        await new LocationSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        (await db.Locations.IgnoreQueryFilters().CountAsync(l => l.SeedKey == LocationSeeder.SeedKey)).Should().Be(1, "idempotente (CA 3)");
    }

    [Fact]
    public async Task Cadastra_troca_o_padrao_e_recusa_codigo_repetido()
    {
        var warehouse = await ActorAsync("warehouse");
        var code = Code("EXP");
        var first = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/locations", new { code = code.ToLowerInvariant(), name = "Expedição" }));
        (await JsonAsync(await warehouse.GetAsync($"/api/v1/inventory/locations/{first}"))).GetProperty("code").GetString().Should().Be(code, "código em maiúsculas");

        var dup = await warehouse.PostAsJsonAsync("/api/v1/inventory/locations", new { code, name = "Outra" });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(dup)).GetProperty("detail").GetString().Should().Be($"Já existe o local {code}.");

        (await warehouse.PutAsJsonAsync($"/api/v1/inventory/locations/{first}", new { code, name = "Expedição", isDefault = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
        (await scope.ServiceProvider.GetRequiredService<ILocations>().DefaultAsync())!.Id.Should().Be(first, "o novo padrão vem pré-selecionado (CA 4)");
        (await db.Locations.CountAsync(l => l.IsDefault)).Should().Be(1, "um só padrão");

        var inactivateDefault = await warehouse.PostAsync($"/api/v1/inventory/locations/{first}/inactivate", null);
        inactivateDefault.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(inactivateDefault)).GetProperty("detail").GetString().Should().Be("Escolha outro local padrão antes.");

        // Devolve o padrão ao ALM, para não depender da ordem dos testes.
        var almId = await db.Locations.Where(l => l.Code == "ALM").Select(l => l.Id).SingleAsync();
        (await warehouse.PutAsJsonAsync($"/api/v1/inventory/locations/{almId}", new { code = "ALM", name = "Almoxarifado principal", isDefault = true })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Com_saldo_nao_inativa_em_uso_nao_exclui_e_sem_uso_exclui()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var warehouse = await ActorAsync("warehouse");
        var withBalance = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/locations", new { code = Code("SLD"), name = "Com saldo" }));
        var used = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/locations", new { code = Code("USO"), name = "Em uso" }));
        var free = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/locations", new { code = Code("LVR"), name = "Livre" }));
        State[withBalance] = "saldo";
        State[used] = "uso";

        var balance = await warehouse.PostAsync($"/api/v1/inventory/locations/{withBalance}/inactivate", null);
        balance.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(balance)).GetProperty("detail").GetString().Should().Be("O local tem saldo. Transfira o saldo antes.");

        (await warehouse.DeleteAsync($"/api/v1/inventory/locations/{free}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "inventory.delete fica só com o Proprietário (CA 1)");
        var inUse = await owner.DeleteAsync($"/api/v1/inventory/locations/{used}");
        inUse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(inUse);
        problem.GetProperty("code").GetString().Should().Be("location_in_use");
        problem.GetProperty("detail").GetString().Should().Be("O local está em uso. Inative-o.");
        (await owner.PostAsync($"/api/v1/inventory/locations/{used}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK, "a alternativa do diálogo");
        (await owner.DeleteAsync($"/api/v1/inventory/locations/{free}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = await ScopeAsync();
        var contract = scope.ServiceProvider.GetRequiredService<ILocations>();
        (await contract.ActiveAsync()).Should().NotContain(l => l.Id == used || l.Id == free);
        (await contract.FindAsync([used, free])).Should().ContainSingle(l => l.Id == used && !l.IsActive, "inativo resolvido por id; excluído não volta");
        (await scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>().AuditEntries.AnyAsync(a => a.Entity == "Location" && a.EntityId == used.ToString()))
            .Should().BeTrue("auditado (CA 20)");
    }
}
