using System.Net;
using System.Net.Http.Json;
using BemAqui.Inventory.Contracts;
using BemAqui.Parties.Application;
using BemAqui.Parties.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Inventory;

public partial class ItemsTests
{
    [Fact]
    public async Task Codigo_do_fornecedor_exige_o_papel_e_e_unico_por_fornecedor()
    {
        var warehouse = await ActorAsync("warehouse");
        var kg = await UnitIdAsync("KG");
        var itemA = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(kg, code: Code("CHAPA"))));
        var itemB = await CreatedAsync(await warehouse.PostAsJsonAsync("/api/v1/inventory/items", Item(kg)));
        var supplier = await PartyAsync("Aços Brasil", PartyRoleKind.Supplier);
        var customer = await PartyAsync("Condomínio Sol", PartyRoleKind.Customer);

        var notSupplier = await warehouse.PostAsJsonAsync($"/api/v1/inventory/items/{itemA}/supplier-codes", new { partyId = customer, supplierCode = "X1", supplierUnit = (string?)null, conversionFactor = 1m });
        notSupplier.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(notSupplier);
        problem.GetProperty("code").GetString().Should().Be("party_not_supplier");
        problem.GetProperty("detail").GetString().Should().EndWith("não é fornecedor. Adicione o papel Fornecedor no cadastro.");
        (await warehouse.PostAsJsonAsync($"/api/v1/inventory/items/{itemA}/supplier-codes", new { partyId = Guid.CreateVersion7(), supplierCode = "X1", supplierUnit = (string?)null, conversionFactor = 1m }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "party_id validado por IParties (CA 13)");

        await CreatedAsync(await warehouse.PostAsJsonAsync($"/api/v1/inventory/items/{itemA}/supplier-codes", new { partyId = supplier, supplierCode = " 7781 ", supplierUnit = "cx", conversionFactor = 25m }));
        var taken = await warehouse.PostAsJsonAsync($"/api/v1/inventory/items/{itemB}/supplier-codes", new { partyId = supplier, supplierCode = "7781", supplierUnit = "CX", conversionFactor = 25m });
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(taken)).GetProperty("detail").GetString().Should().StartWith("O código 7781 do fornecedor Aços Brasil").And.Contain("já está ligado ao item CHAPA-");

        await using var scope = await ScopeAsync();
        var codes = scope.ServiceProvider.GetRequiredService<IItemSupplierCodes>();
        var match = await codes.FindAsync(supplier, "7781");
        match.Should().NotBeNull();
        match!.ItemId.Should().Be(itemA);
        match.SupplierUnit.Should().Be("CX");
        match.ConversionFactor.Should().Be(25m);
        (await codes.FindAsync(supplier, "0000")).Should().BeNull();

        // Terceiro com código não é excluído (só inativado); excluído por fora, o vínculo aparece marcado e o Find não casa (CA 12).
        var usages = new List<string?>();
        foreach (var provider in scope.ServiceProvider.GetRequiredService<IEnumerable<IPartyUsageProvider>>())
        {
            usages.Add(await provider.UsageAsync(supplier));
        }

        usages.Should().Contain("códigos de fornecedor dos itens");
        var parties = scope.ServiceProvider.GetRequiredService<IPartiesDb>();
        var party = await parties.Parties.SingleAsync(p => p.Id == supplier);
        parties.Parties.Remove(party);
        await parties.SaveChangesAsync();
        (await codes.FindAsync(supplier, "7781")).Should().BeNull();
        var list = await JsonAsync(await warehouse.GetAsync($"/api/v1/inventory/items/{itemA}/supplier-codes"));
        list[0].GetProperty("partyDeleted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Opcoes_de_fornecedor_so_trazem_fornecedores_ativos()
    {
        var warehouse = await ActorAsync("warehouse");
        var tag = Guid.NewGuid().ToString("N")[..5];
        await PartyAsync("Fornecedor " + tag, PartyRoleKind.Supplier);
        await PartyAsync("Cliente " + tag, PartyRoleKind.Customer);
        var options = await JsonAsync(await warehouse.GetAsync($"/api/v1/inventory/supplier-options?q={tag}"));
        options.GetArrayLength().Should().Be(1);
        options[0].GetProperty("name").GetString().Should().StartWith("Fornecedor " + tag);
    }
}
