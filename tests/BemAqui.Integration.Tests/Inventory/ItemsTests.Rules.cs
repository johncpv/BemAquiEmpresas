using System.Net;
using System.Net.Http.Json;
using BemAqui.Inventory.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

public partial class ItemsTests
{
    [Fact]
    public async Task Item_ativo_de_materia_prima_exige_ncm_e_consumo_e_servico_nao()
    {
        var warehouse = await ActorAsync("warehouse");
        var un = await UnitIdAsync();
        var noNcm = await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(un, ncm: null));
        noNcm.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await noNcm.Content.ReadAsStringAsync()).Should().Contain("Preencha o NCM (8 dígitos) para o item ativo.");
        (await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(un, ncm: "8431.31"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "o NCM tem 8 dígitos");
        await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(un, type: "Consumable", ncm: null)));

        var service = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(un, type: "Service", ncm: null, lot: true, serial: true)));
        var detail = await JsonAsync(await warehouse.GetAsync($"/api/v1/inventory/items/{service}"));
        detail.GetProperty("tracksLot").GetBoolean().Should().BeFalse("serviço não controla lote nem série (CA 9)");
        detail.GetProperty("minStock").GetDecimal().Should().Be(0m);

        var raw = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(un, ncm: "8431.31.10", lot: true, warranty: 6)));
        var saved = await JsonAsync(await warehouse.GetAsync($"/api/v1/inventory/items/{raw}"));
        saved.GetProperty("ncm").GetString().Should().Be("84313110", "só os dígitos");
        saved.GetProperty("minStock").GetDecimal().Should().Be(10.5m);
        saved.GetProperty("warrantyMonths").GetInt32().Should().Be(6, "a garantia do item sobrescreve o padrão (CA 12)");
        saved.GetProperty("averageCost").GetDecimal().Should().Be(0m);
    }

    [Fact]
    public async Task Custo_medio_no_corpo_e_recusado_e_codigo_repetido_concorrente_da_409()
    {
        var warehouse = await ActorAsync("warehouse");
        var un = await UnitIdAsync();
        var withCost = Item(un);
        withCost["averageCost"] = 99m;
        (await warehouse.PostAsJsonAsync("/api/v1/inventory/items", withCost)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "custo médio é do EST-01 (CA 13)");

        var code = Code("CMP");
        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(un, type: "Component", code: code))));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "o índice único garante em gravação simultânea (CA 15)");
        var conflict = responses.First(r => r.StatusCode != HttpStatusCode.Created);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(conflict)).GetProperty("detail").GetString().Should().Be($"Já existe o item {code}.");
    }

    [Fact]
    public async Task Com_movimento_nao_troca_unidade_nem_desliga_lote()
    {
        var warehouse = await ActorAsync("warehouse");
        var un = await UnitIdAsync();
        var kg = await UnitIdAsync("KG");
        var code = Code("MP");
        var id = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(un, code: code, lot: true)));
        State[id] = "movimento";

        var unit = await warehouse.PutAsJsonAsync($"/api/v1/inventory/items/{id}", Item(kg, code: code, lot: true));
        unit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(unit)).GetProperty("detail").GetString().Should().Be("Item com movimentação não pode trocar a unidade de estoque.");
        var lot = await warehouse.PutAsJsonAsync($"/api/v1/inventory/items/{id}", Item(un, code: code, lot: false));
        (await JsonAsync(lot)).GetProperty("code").GetString().Should().Be("item_lot_locked");
        (await warehouse.PutAsJsonAsync($"/api/v1/inventory/items/{id}", Item(un, code: code, lot: true, serial: true))).StatusCode.Should().Be(HttpStatusCode.OK, "ligar a série pode");
    }

    [Fact]
    public async Task Inativar_excluir_e_reativar_sem_ncm()
    {
        var owner = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var warehouse = await ActorAsync("warehouse");
        var un = await UnitIdAsync();
        var balance = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(un)));
        var used = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(un)));
        var freeCode = Code("MP");
        var free = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(un, code: freeCode)));
        State[balance] = "saldo";
        State[used] = "uso";

        (await warehouse.PostAsync($"/api/v1/inventory/items/{balance}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "item com saldo não inativa (CA 16)");
        (await warehouse.DeleteAsync($"/api/v1/inventory/items/{free}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "inventory.delete só no Proprietário");
        var inUse = await owner.DeleteAsync($"/api/v1/inventory/items/{used}");
        inUse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(inUse)).GetProperty("detail").GetString().Should().Be("O item está em uso. Inative-o.");

        // Inativo pode ficar sem NCM; reativar exige o NCM (CA 7).
        (await warehouse.PostAsync($"/api/v1/inventory/items/{free}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await warehouse.PutAsJsonAsync($"/api/v1/inventory/items/{free}", Item(un, code: freeCode, ncm: null))).StatusCode.Should().Be(HttpStatusCode.OK);
        var reactivate = await warehouse.PostAsync($"/api/v1/inventory/items/{free}/reactivate", null);
        reactivate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await reactivate.Content.ReadAsStringAsync()).Should().Contain("Preencha o NCM");

        (await owner.DeleteAsync($"/api/v1/inventory/items/{free}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await using var scope = await ScopeAsync();
        var items = scope.ServiceProvider.GetRequiredService<IItems>();
        (await items.FindAsync([free])).Should().ContainSingle(i => i.IsDeleted, "resolução por id inclui excluídos (CA 21)");
        (await items.SearchAsync(null, freeCode)).Should().BeEmpty("a busca só traz ativos");
        (await items.SearchAsync(ItemType.RawMaterial, null, 5)).Should().OnlyContain(i => i.Type == ItemType.RawMaterial && i.IsActive);
        (await scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>().AuditEntries.AnyAsync(a => a.Entity == "Item" && a.EntityId == free.ToString()))
            .Should().BeTrue("auditado (CA 23)");
    }
}
