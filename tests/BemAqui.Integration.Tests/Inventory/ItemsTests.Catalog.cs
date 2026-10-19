using System.Net;
using System.Net.Http.Json;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Inventory.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

public partial class ItemsTests
{
    [Fact]
    public async Task Seeds_de_unidades_e_garantia_nao_sobrescrevem_nem_recriam()
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
        (await db.Units.Where(u => u.SeedKey != null).Select(u => u.Code).ToListAsync()).Should().BeEquivalentTo(["UN", "KG", "M", "M2", "M3", "L", "PC"]);
        var defaults = await db.WarrantyDefaults.ToDictionaryAsync(w => w.ItemType, w => w.Months);
        defaults.Should().BeEquivalentTo(new Dictionary<ItemType, int?> { [ItemType.Finished] = 12, [ItemType.Component] = 3, [ItemType.Service] = 3, [ItemType.RawMaterial] = null, [ItemType.Consumable] = null });

        var owner = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        (await owner.PutAsJsonAsync("/api/v1/inventory/warranty-defaults/Finished", new { months = 24 })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PutAsJsonAsync("/api/v1/inventory/warranty-defaults/Finished", new { months = 121 })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await new UnitSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        await new WarrantyDefaultSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        (await db.WarrantyDefaults.AsNoTracking().SingleAsync(w => w.ItemType == ItemType.Finished)).Months.Should().Be(24, "o seed não sobrescreve o editado (CA 4)");
        (await db.Units.IgnoreQueryFilters().CountAsync(u => u.SeedKey != null)).Should().Be(7);
        (await owner.PutAsJsonAsync("/api/v1/inventory/warranty-defaults/Finished", new { months = 12 })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unidade_em_uso_nao_inativa_nem_exclui()
    {
        var owner = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var unitCode = "U" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var unit = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/inventory/units", new { code = unitCode.ToLowerInvariant(), name = "Rolo" }));
        (await owner.PostAsJsonAsync("/api/v1/inventory/units", new { code = unitCode, name = "Outro" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.PostAsJsonAsync("/api/v1/inventory/units", new { code = "MUITOLONGO", name = "Longo" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/inventory/items", Item(unit)));
        await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/inventory/items", Item(unit)));

        var inactivate = await owner.PostAsync($"/api/v1/inventory/units/{unit}/inactivate", null);
        inactivate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(inactivate)).GetProperty("detail").GetString().Should().Be("A unidade está em uso por 2 itens.");
        (await owner.DeleteAsync($"/api/v1/inventory/units/{unit}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Categorias_unicas_entre_irmas_sem_ciclo_e_com_filha_so_inativa()
    {
        var owner = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var root = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/inventory/categories", new { name = "Chapas " + Guid.NewGuid().ToString("N")[..4], parentId = (Guid?)null }));
        var child = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/inventory/categories", new { name = "Aço carbono", parentId = root }));
        (await owner.PostAsJsonAsync("/api/v1/inventory/categories", new { name = "ACO CARBONO", parentId = root })).StatusCode.Should().Be(HttpStatusCode.Conflict, "nome único entre as irmãs, sem acento nem caixa");
        var otherRoot = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/inventory/categories", new { name = "Aço carbono", parentId = (Guid?)null }));
        otherRoot.Should().NotBeEmpty("o mesmo nome em outro nível vale");

        var cycle = await owner.PutAsJsonAsync($"/api/v1/inventory/categories/{root}", new { name = "Chapas", parentId = child });
        cycle.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "a árvore não aceita ciclo (CA 3)");
        var delete = await owner.DeleteAsync($"/api/v1/inventory/categories/{root}");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(delete)).GetProperty("code").GetString().Should().Be("category_in_use");
        (await owner.PostAsync($"/api/v1/inventory/categories/{root}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/api/v1/inventory/categories/{otherRoot}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
