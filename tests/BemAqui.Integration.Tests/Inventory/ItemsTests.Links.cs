using System.Net;
using System.Net.Http.Json;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using BemAqui.Parties.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

public partial class ItemsTests
{
    private async Task<Guid> PartyAsync(string name, params PartyRoleKind[] roles)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IPartiesDb>();
        var party = new Party { Kind = PartyKind.PJ, LegalName = name + " " + Guid.NewGuid().ToString("N")[..4], CreatedAt = DateTimeOffset.UtcNow };
        foreach (var role in roles)
        {
            party.Roles.Add(new PartyRole { PartyId = party.Id, Role = role });
        }

        db.Parties.Add(party);
        await db.SaveChangesAsync();
        return party.Id;
    }

    [Fact]
    public async Task Conversao_da_chapa_converte_nos_dois_sentidos_e_recusa_par_repetido()
    {
        var warehouse = await ActorAsync("warehouse");
        var kg = await UnitIdAsync("KG");
        var m2 = await UnitIdAsync("M2");
        var m = await UnitIdAsync("M");
        var item = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(kg)));

        var conversion = await CreatedAsync(await warehouse.PostAsJsonAsync($"/api/v1/inventory/items/{item}/conversions", new { fromUnitId = m2, toUnitId = kg, factor = 15.7m }));
        var reverse = await warehouse.PostAsJsonAsync($"/api/v1/inventory/items/{item}/conversions", new { fromUnitId = kg, toUnitId = m2, factor = 0.06m });
        reverse.StatusCode.Should().Be(HttpStatusCode.Conflict, "a inversa é calculada, não gravada (CA 2)");
        (await JsonAsync(reverse)).GetProperty("detail").GetString().Should().Be("Este item já tem conversão entre KG e M2.");
        (await warehouse.PostAsJsonAsync($"/api/v1/inventory/items/{item}/conversions", new { fromUnitId = m2, toUnitId = m, factor = 2m })).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "uma das unidades é a de estoque");
        (await warehouse.PostAsJsonAsync($"/api/v1/inventory/items/{item}/conversions", new { fromUnitId = m, toUnitId = kg, factor = 0m })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        await using var scope = await ScopeAsync();
        var convert = scope.ServiceProvider.GetRequiredService<IUnitConversion>();
        (await convert.ConvertAsync(item, 31.4m, kg, m2)).Value.Should().Be(2.0000m);
        (await convert.ConvertAsync(item, 2m, m2, kg)).Value.Should().Be(31.4000m);
        (await convert.ConvertAsync(item, 5m, kg, kg)).Value.Should().Be(5m);
        var missing = await convert.ConvertAsync(item, 1m, m, kg);
        missing.IsSuccess.Should().BeFalse();
        missing.Error!.Code.Should().Be("unit_conversion_missing");
        missing.Error.Message.Should().Contain("não tem conversão de M para KG");

        // Trocar o fator vale para os próximos documentos (CA 7) e fica no histórico (CA 16).
        (await warehouse.PutAsJsonAsync($"/api/v1/inventory/items/{item}/conversions/{conversion}", new { fromUnitId = m2, toUnitId = kg, factor = 15.75m })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>().AuditEntries.CountAsync(a => a.Entity == "ItemUnitConversion" && a.EntityId == conversion.ToString())).Should().Be(2);
    }

    [Fact]
    public async Task Rotas_filhas_conferem_o_item_da_url()
    {
        var warehouse = await ActorAsync("warehouse");
        var kg = await UnitIdAsync("KG");
        var m2 = await UnitIdAsync("M2");
        var itemA = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(kg)));
        var itemB = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(kg)));
        var conversion = await CreatedAsync(await warehouse.PostAsJsonAsync($"/api/v1/inventory/items/{itemA}/conversions", new { fromUnitId = m2, toUnitId = kg, factor = 10m }));
        var supplier = await PartyAsync("Aços Brasil", PartyRoleKind.Supplier);
        var code = await CreatedAsync(await warehouse.PostAsJsonAsync($"/api/v1/inventory/items/{itemA}/supplier-codes", new { partyId = supplier, supplierCode = "CH-2", supplierUnit = "kg", conversionFactor = 1m }));

        (await warehouse.PutAsJsonAsync($"/api/v1/inventory/items/{itemB}/conversions/{conversion}", new { fromUnitId = m2, toUnitId = kg, factor = 1m })).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR pai/filho (CA 13)");
        (await warehouse.DeleteAsync($"/api/v1/inventory/items/{itemB}/conversions/{conversion}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await warehouse.DeleteAsync($"/api/v1/inventory/items/{itemB}/supplier-codes/{code}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await warehouse.DeleteAsync($"/api/v1/inventory/items/{itemA}/conversions/{conversion}")).StatusCode.Should().Be(HttpStatusCode.NoContent, "remover (CA 14)");
        (await JsonAsync(await warehouse.GetAsync($"/api/v1/inventory/items/{itemA}/conversions"))).GetArrayLength().Should().Be(0);

        var viewer = await ActorAsync("finance");
        (await viewer.GetAsync($"/api/v1/inventory/items/{itemA}/supplier-codes")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.DeleteAsync($"/api/v1/inventory/items/{itemA}/supplier-codes/{code}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "só leitura sem inventory.edit (CA 15)");
    }
}
