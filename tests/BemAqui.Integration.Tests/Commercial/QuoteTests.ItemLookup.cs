using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Catalog.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Inventory.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>PRC-00: o "Adicionar item" do orçamento busca o item cadastrado (código, nome e GTIN) e grava o vínculo.</summary>
public partial class QuoteTests
{
    private const string ItemLookup = Base + "/lookups/items";

    /// <summary>Item ativo com custo médio e, opcionalmente, um produto do Catálogo com GTIN-13 válido e único.</summary>
    private Task<(Guid Item, string Code, string? Gtin)> LookupItemAsync(ItemType type, decimal averageCost, bool withGtin = false, bool active = true) => WithDbAsync(async db =>
    {
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var code = "PRC-" + suffix;
        var item = new BemAqui.Inventory.Domain.Item { Type = type, Code = code, Name = "Botoeira z" + suffix, UnitId = unit, IsActive = active, CreatedAt = DateTimeOffset.UtcNow };
        item.ApplyAverageCost(averageCost);
        db.Items.Add(item);
        string? gtin = null;
        if (withGtin)
        {
            var body = "789" + Random.Shared.NextInt64(100_000_000, 999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
            gtin = body + GtinRules.CheckDigit(body).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var category = await db.ProductCategories.Select(c => c.Id).FirstOrDefaultAsync();
            if (category == Guid.Empty)
            {
                var created = new ProductCategory { Name = "Geral PRC", CreatedAt = DateTimeOffset.UtcNow };
                db.ProductCategories.Add(created);
                category = created.Id;
            }

            var product = new Product { ItemId = item.Id, CategoryId = category, SaleName = item.Name, CreatedAt = DateTimeOffset.UtcNow };
            db.Products.Add(product);
            db.ProductBarcodes.Add(new ProductBarcode { ProductId = product.Id, Gtin = gtin, CreatedAt = DateTimeOffset.UtcNow });
        }

        await db.SaveChangesAsync();
        return (item.Id, code, gtin);
    });

    private static async Task<JsonElement[]> OptionsAsync(HttpClient client, string term)
    {
        var response = await client.GetAsync($"{ItemLookup}?q={Uri.EscapeDataString(term)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await JsonAsync(response)).EnumerateArray().ToArray();
    }

    [Fact]
    public async Task Busca_por_codigo_nome_e_GTIN_com_tipo_unidade_e_custo_so_para_quem_ve_custo()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var seller = await NoCostActorAsync();
        var (item, code, gtin) = await LookupItemAsync(ItemType.Finished, 123.456789m, withGtin: true);
        var lists = await WithDbAsync(db => db.PriceLists.IgnoreQueryFilters().CountAsync());

        var byCode = (await OptionsAsync(owner, code)).Single(o => o.GetProperty("id").GetGuid() == item);
        byCode.GetProperty("type").GetString().Should().Be("Finished");
        byCode.GetProperty("unitCode").GetString().Should().Be("UN");
        byCode.GetProperty("averageCost").GetDecimal().Should().Be(123.456789m, "CA 2: com quotes.view_cost");
        (await OptionsAsync(owner, "botoeira z" + code[4..])).Should().Contain(o => o.GetProperty("id").GetGuid() == item, "busca pelo nome");

        var byGtin = await OptionsAsync(owner, gtin!);
        byGtin[0].GetProperty("id").GetGuid().Should().Be(item, "CA 1: o GTIN do produto acha o item, no topo");

        var hidden = (await OptionsAsync(seller, code)).Single(o => o.GetProperty("id").GetGuid() == item);
        hidden.GetProperty("averageCost").ValueKind.Should().Be(JsonValueKind.Null, "CA 2: sem quotes.view_cost o custo não sai da API");

        (await WithDbAsync(db => db.PriceLists.IgnoreQueryFilters().CountAsync())).Should().Be(lists, "CA 9: a busca não cria tabela de preço");
    }

    [Fact]
    public async Task Busca_tem_limite_e_ignora_inativo()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (inactive, code, _) = await LookupItemAsync(ItemType.Component, 0m, active: false);
        (await OptionsAsync(owner, code)).Should().NotContain(o => o.GetProperty("id").GetGuid() == inactive);
        for (var i = 0; i < 22; i++)
        {
            await LookupItemAsync(ItemType.Component, 1m);
        }

        (await OptionsAsync(owner, "PRC-")).Length.Should().Be(20, "CA 7: no máximo 20");
        (await OptionsAsync(owner, new string('x', 500))).Should().BeEmpty("termo cortado em 100 caracteres, sem erro");
    }

    [Fact]
    public async Task Adicionar_item_ligado_grava_vinculo_e_tipo_pelo_item_e_recusa_item_invalido()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var customer = await CustomerAsync(owner);
        var quote = await JsonAsync(await owner.SendAsync(Post(Base, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        var (service, _, _) = await LookupItemAsync(ItemType.Service, 0m);
        var (finished, _, _) = await LookupItemAsync(ItemType.Finished, 0m);
        var (raw, _, _) = await LookupItemAsync(ItemType.RawMaterial, 0m);

        // CA 3 e 6: o tipo sai do item, qualquer que seja o enviado.
        foreach (var (linked, expected) in new[] { (service, "Service"), (finished, "Equipment"), (raw, "Product") })
        {
            var added = await owner.PostAsJsonAsync($"{Base}/{id}/items", new { kind = "Equipment", itemId = linked, description = "Ligado", quantity = 2m, unitPrice = (decimal?)null, resetPrice = false, version = Version(quote) });
            added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
            quote = await JsonAsync(added);
            var line = quote.GetProperty("items").EnumerateArray().Last();
            line.GetProperty("itemId").GetGuid().Should().Be(linked);
            line.GetProperty("kind").GetString().Should().Be(expected);
            line.GetProperty("unitCode").GetString().Should().Be("UN");
        }

        // CA 5: inexistente (como o de outro tenant, que mora noutro banco) e inativo dão 422 no campo itemId.
        foreach (var bad in new[] { Guid.CreateVersion7(), (await LookupItemAsync(ItemType.Component, 0m, active: false)).Item })
        {
            var refused = await owner.PostAsJsonAsync($"{Base}/{id}/items", new { kind = "Product", itemId = bad, description = "X", quantity = 1m, unitPrice = (decimal?)null, resetPrice = false, version = Version(quote) });
            refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await JsonAsync(refused)).GetProperty("errors").TryGetProperty("itemId", out _).Should().BeTrue();
        }

        // CA 5: a linha já ligada continua editável depois que o item é inativado.
        await WithDbAsync(async db => { (await db.Items.SingleAsync(i => i.Id == raw)).IsActive = false; return await db.SaveChangesAsync(); });
        var rawLine = quote.GetProperty("items").EnumerateArray().Single(l => l.GetProperty("itemId").GetGuid() == raw).GetProperty("id").GetGuid();
        var kept = await owner.PutAsJsonAsync($"{Base}/{id}/items/{rawLine}", new { kind = "Product", itemId = raw, description = "Ligado editado", quantity = 3m, unitPrice = (decimal?)null, resetPrice = false, version = Version(quote) });
        kept.StatusCode.Should().Be(HttpStatusCode.OK, await kept.Content.ReadAsStringAsync());
    }
}
