using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Catalog.Contracts;
using BemAqui.Inventory.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Catalog;

public partial class CatalogTests
{
    /// <summary>Fábrica (a Konebras): o módulo não é liberado (403) e fica oculto no menu; a ajuda do catálogo some.</summary>
    [Fact]
    public async Task Fabrica_nao_ve_nem_acessa_o_catalogo()
    {
        var (_, owner) = await ActorAsync("owner", tenancy.TenantB, "b.localhost");
        var denied = await owner.GetAsync(Products);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(denied)).Should().Be("module_not_released");
        var me = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me/permissions");
        var catalog = me.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("code").GetString() == "catalog");
        catalog.GetProperty("hidden").GetBoolean().Should().BeTrue();
        catalog.GetProperty("released").GetBoolean().Should().BeFalse();
        (await owner.GetAsync("/api/v1/ajuda/catalogo-produtos")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a ajuda some com o módulo oculto");
    }

    /// <summary>CA 3 e 4: o item nasce no mesmo formulário (Mercadoria, mesmo nome, unidade e NCM); o mesmo item não ganha outro produto.</summary>
    [Fact]
    public async Task Cria_produto_com_item_novo_e_o_item_nao_ganha_segundo_produto()
    {
        var (_, owner) = await ActorAsync("owner");
        var name = Unique("Refrigerante cola 2 L");
        var unit = await UnitAsync(owner);
        var created = await CreatedAsync(await owner.PostAsJsonAsync(Products, NewProduct(name, unit, ncm: "22021000")));
        var itemId = created.GetProperty("itemId").GetGuid();
        created.GetProperty("categoryName").GetString().Should().Be("Geral", "a categoria padrão (CA 14)");
        created.GetProperty("itemCode").GetString().Should().StartWith("MER-");
        created.GetProperty("ncm").GetString().Should().Be("22021000");

        await using (var scope = await ScopeAsync())
        {
            var item = (await scope.ServiceProvider.GetRequiredService<IItems>().FindAsync([itemId])).Single();
            item.Type.Should().Be(ItemType.Merchandise);
            item.Name.Should().Be(name, "o nome é digitado uma vez só");
            item.UnitId.Should().Be(unit);
        }

        var second = await owner.PostAsJsonAsync(Products, new
        {
            saleName = Unique("Outro"), categoryId = (Guid?)null, itemId, newItem = (object?)null, plu = (string?)null, isWeighable = false, sellablePos = true,
            sellableOnline = false, price = (decimal?)null, barcodes = Array.Empty<object>(),
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(second)).GetProperty("detail").GetString().Should().Be("Este item já tem um produto no catálogo.");
        (await owner.DeleteAsync($"/api/v1/inventory/items/{itemId}")).StatusCode.Should().Be(HttpStatusCode.Conflict, "item com produto não é excluído no Estoque");
    }

    /// <summary>CA 5, 6 e 7: vários códigos com a quantidade da embalagem; dígito errado = 422; GTIN de outro produto = 409 com o nome dele.</summary>
    [Fact]
    public async Task Codigos_de_barras_validam_o_digito_e_a_unicidade()
    {
        var (_, owner) = await ActorAsync("owner");
        var unit = await UnitAsync(owner);
        var unitGtin = NewGtin();
        var first = await CreatedAsync(await owner.PostAsJsonAsync(Products, NewProduct(Unique("Água 500 ml"), unit, gtin: unitGtin)));
        var id = first.GetProperty("id").GetGuid();
        var box = await CreatedAsync(await owner.PostAsJsonAsync($"{Products}/{id}/barcodes", new { gtin = NewGtin(14), packQuantity = 12m }));
        box.GetProperty("packQuantity").GetDecimal().Should().Be(12m);
        (await owner.GetFromJsonAsync<JsonElement>($"{Products}/{id}/barcodes")).GetArrayLength().Should().Be(2);

        var wrong = unitGtin[..^1] + ((unitGtin[^1] - '0' + 1) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var invalid = await owner.PostAsJsonAsync($"{Products}/{id}/barcodes", new { gtin = wrong, packQuantity = 1m });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(invalid)).GetProperty("detail").GetString().Should().Be("Código de barras inválido. Confira os dígitos.");

        var otherName = Unique("Água com gás");
        var taken = await owner.PostAsJsonAsync(Products, NewProduct(otherName, unit, gtin: unitGtin));
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(taken);
        Code(problem).Should().Be("barcode_taken");
        problem.GetProperty("detail").GetString().Should().Contain(first.GetProperty("saleName").GetString()!);
        (await QueryAsync(db => db.Products.CountAsync(p => p.SaleName == otherName))).Should().Be(0, "nada é gravado: nem o produto nem o item");
        (await QueryAsync(db => db.Items.CountAsync(i => i.Name == otherName))).Should().Be(0);
    }

    /// <summary>
    /// CA 8: com catalog.edit_price, a versão da tabela sobe, o histórico guarda anterior, novo, quem e quando, e o PriceChanged vai ao outbox.
    /// </summary>
    [Fact]
    public async Task Alterar_preco_sobe_a_versao_guarda_o_historico_e_publica_o_evento()
    {
        var (ownerId, owner) = await ActorAsync("owner");
        var product = await CreatedAsync(await owner.PostAsJsonAsync(Products, NewProduct(Unique("Café 500 g"), await UnitAsync(owner), price: 18.90m)));
        var id = product.GetProperty("id").GetGuid();
        var version = product.GetProperty("priceListVersion").GetInt64();
        product.GetProperty("price").GetDecimal().Should().Be(18.90m);

        var changed = await owner.PutAsJsonAsync($"{Products}/{id}/price", new { price = 21.50m, priceListId = (Guid?)null, applyToVariants = false });
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        var after = await JsonAsync(changed);
        after.GetProperty("price").GetDecimal().Should().Be(21.50m);
        after.GetProperty("priceListVersion").GetInt64().Should().Be(version + 1);

        var history = await owner.GetFromJsonAsync<JsonElement>($"{Products}/{id}/price-history");
        history.GetArrayLength().Should().Be(2);
        var last = history[0];
        (last.GetProperty("previousPrice").GetDecimal(), last.GetProperty("newPrice").GetDecimal(), last.GetProperty("listVersion").GetInt64()).Should().Be((18.90m, 21.50m, version + 1));
        last.GetProperty("changedByName").ValueKind.Should().NotBe(JsonValueKind.Null);
        (await QueryAsync(db => db.PriceChanges.Where(c => c.ProductId == id).OrderByDescending(c => c.ListVersion).Select(c => c.ChangedBy).FirstAsync())).Should().Be(ownerId);

        await using var scope = await ScopeAsync();
        var outbox = (await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().OutboxMessages.AsNoTracking()
            .Where(m => m.Type.EndsWith(nameof(PriceChanged))).ToListAsync()).Where(m => m.Payload.Contains(id.ToString(), StringComparison.Ordinal)).ToList();
        outbox.Should().HaveCount(2);
        var payload = outbox.Select(m => JsonDocument.Parse(m.Payload).RootElement).Single(p => p.GetProperty("NewPrice").GetDecimal() == 21.50m);
        payload.GetProperty("PreviousPrice").GetDecimal().Should().Be(18.90m);
        payload.GetProperty("Version").GetInt64().Should().Be(version + 1);
    }

    /// <summary>
    /// CA 9 e 10 (segurança): quem edita e não tem edit_price recebe 403 ao mandar preço e nada muda; sem view_cost, custo e margem nem vêm
    /// na resposta. CA 11: com view_cost, o painel traz custo e margem.
    /// </summary>
    [Fact]
    public async Task Preco_exige_edit_price_e_custo_so_vem_com_view_cost()
    {
        var (_, owner) = await ActorAsync("owner");
        var name = Unique("Biscoito");
        var product = await CreatedAsync(await owner.PostAsJsonAsync(Products, NewProduct(name, await UnitAsync(owner), price: 5m)));
        var id = product.GetProperty("id").GetGuid();
        product.TryGetProperty("cost", out var ownerCost).Should().BeTrue("o Proprietário vê custo");
        ownerCost.GetProperty("marginPercent").GetDecimal().Should().Be(100m, "sem custo de entrada, toda a venda é margem");

        var (_, editor) = await ActorAsync("view,edit");
        var input = new { saleName = name, categoryId = (Guid?)null, plu = (string?)null, isWeighable = false, sellablePos = true, sellableOnline = false, price = 1m, version = product.GetProperty("version").GetUInt32() };
        var forbidden = await editor.PutAsJsonAsync($"{Products}/{id}", input);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(forbidden)).Should().Be("forbidden");
        (await editor.PutAsJsonAsync($"{Products}/{id}/price", new { price = 1m, priceListId = (Guid?)null, applyToVariants = false })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.GetFromJsonAsync<JsonElement>($"{Products}/{id}")).GetProperty("price").GetDecimal().Should().Be(5m, "nada muda");

        var detail = await editor.GetStringAsync($"{Products}/{id}");
        JsonDocument.Parse(detail).RootElement.TryGetProperty("cost", out _).Should().BeFalse("ausente, não zerado (CA 10)");
        var list = await editor.GetFromJsonAsync<JsonElement>($"{Products}?q={Uri.EscapeDataString(name)}");
        list.GetProperty("items").EnumerateArray().Should().ContainSingle().Which.TryGetProperty("cost", out _).Should().BeFalse();

        var (_, analyst) = await ActorAsync("view,view_cost");
        var seen = await analyst.GetFromJsonAsync<JsonElement>($"{Products}/{id}");
        seen.GetProperty("cost").GetProperty("averageCost").GetDecimal().Should().Be(0m);
        seen.GetProperty("cost").GetProperty("marginPercent").GetDecimal().Should().Be(100m);
    }

    /// <summary>CA 12: por GTIN de embalagem (com a quantidade), por PLU e por trecho do nome, com o preço vigente da tabela padrão.</summary>
    [Fact]
    public async Task Consulta_rapida_por_gtin_de_embalagem_plu_e_nome()
    {
        var (_, owner) = await ActorAsync("owner");
        var name = Unique("Cerveja lata 350 ml");
        var plu = NewPlu();
        var product = await CreatedAsync(await owner.PostAsJsonAsync(Products, NewProduct(name, await UnitAsync(owner), price: 4.99m, gtin: NewGtin(), plu: plu)));
        var id = product.GetProperty("id").GetGuid();
        var pack = NewGtin(14);
        await CreatedAsync(await owner.PostAsJsonAsync($"{Products}/{id}/barcodes", new { gtin = pack, packQuantity = 12m }));

        await using var scope = await ScopeAsync();
        var lookup = scope.ServiceProvider.GetRequiredService<ICatalogLookup>();
        var byPack = await lookup.ByGtinAsync(pack);
        byPack.Should().NotBeNull();
        (byPack!.Product.ProductId, byPack.PackQuantity, byPack.Product.Price, byPack.Match).Should().Be((id, 12m, 4.99m, CatalogLookupMatch.Gtin));
        byPack.Product.UnitCode.Should().Be("UN");
        (await lookup.ByPluAsync(plu))!.Product.ProductId.Should().Be(id);
        (await lookup.SearchAsync(name[^6..])).Should().ContainSingle(h => h.Product.ProductId == id);

        var api = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/catalog/lookup?code={pack}");
        api.EnumerateArray().Should().ContainSingle().Which.GetProperty("packQuantity").GetDecimal().Should().Be(12m);
    }

    /// <summary>
    /// CA 15 e 16: a grade gera uma variação por combinação (item, código e preço próprios), a lista agrupa no pai, a consulta acha a variação,
    /// o pai renomeado renomeia as variações e o contrato ISellableProducts devolve a variação e deixa o pai de fora.
    /// </summary>
    [Fact]
    public async Task Grade_gera_variacoes_que_herdam_do_pai_e_vendem_pelo_contrato()
    {
        var (_, owner) = await ActorAsync("owner");
        var name = Unique("Camiseta básica");
        var parent = await CreatedAsync(await owner.PostAsJsonAsync(Products, NewProduct(name, await UnitAsync(owner), price: 39.90m)));
        var id = parent.GetProperty("id").GetGuid();
        var grade = await owner.PostAsJsonAsync($"{Products}/{id}/variants", new
        {
            attributes = new[] { new { name = "Cor", values = new[] { "Azul", "Preto" } }, new { name = "Tamanho", values = new[] { "P", "M" } } }, price = (decimal?)null,
        });
        grade.StatusCode.Should().Be(HttpStatusCode.OK, await grade.Content.ReadAsStringAsync());
        var withGrade = await JsonAsync(grade);
        withGrade.GetProperty("hasVariants").GetBoolean().Should().BeTrue();
        var variants = withGrade.GetProperty("variants").EnumerateArray().ToList();
        variants.Should().HaveCount(4);
        variants.Should().OnlyContain(v => v.GetProperty("price").GetDecimal() == 39.90m, "a grade nasce com o preço do pai");
        variants.Select(v => v.GetProperty("itemId").GetGuid()).Distinct().Should().HaveCount(4, "cada variação tem item próprio");
        var azulM = variants.Single(v => v.GetProperty("saleName").GetString() == name + " Azul M").GetProperty("id").GetGuid();

        (await owner.PostAsJsonAsync($"{Products}/{id}/variants", new { attributes = new[] { new { name = "Cor", values = new[] { "Azul" } }, new { name = "Tamanho", values = new[] { "P" } } }, price = (decimal?)null }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "combinação repetida não duplica");

        var gtin = NewGtin();
        await CreatedAsync(await owner.PostAsJsonAsync($"{Products}/{azulM}/barcodes", new { gtin, packQuantity = 1m }));
        var list = await owner.GetFromJsonAsync<JsonElement>($"{Products}?q={gtin}");
        list.GetProperty("items").EnumerateArray().Should().ContainSingle().Which.GetProperty("variantCount").GetInt32().Should().Be(4, "a lista mostra o pai com as variações agrupadas");

        var renamed = Unique("Camiseta gola V");
        var put = await owner.PutAsJsonAsync($"{Products}/{id}", new { saleName = renamed, categoryId = (Guid?)null, plu = (string?)null, isWeighable = false, sellablePos = true, sellableOnline = false, price = (decimal?)null, version = withGrade.GetProperty("version").GetUInt32() });
        put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());

        await using var scope = await ScopeAsync();
        var hit = await scope.ServiceProvider.GetRequiredService<ICatalogLookup>().ByGtinAsync(gtin);
        hit!.Product.ProductId.Should().Be(azulM);
        hit.Product.Description.Should().Be(renamed + " Azul M", "o nome do pai desce para a variação (CA 16)");
        var sellable = await scope.ServiceProvider.GetRequiredService<ISellableProducts>().FindManyAsync([id, azulM]);
        sellable.Should().ContainSingle().Which.Should().Match<SellableProduct>(p => p.ProductId == azulM && p.ParentProductId == id && p.Price == 39.90m && p.IsActive);
        (await scope.ServiceProvider.GetRequiredService<ISellableProducts>().FindAsync(id)).Should().BeNull("o pai de grade não é vendido");
    }

    /// <summary>CA 14: categoria com produto não é excluída (409) e a Geral não sai; produto vendido não é excluído, só desativado.</summary>
    [Fact]
    public async Task Categoria_com_produto_e_produto_vendido_so_desativam()
    {
        var (_, owner) = await ActorAsync("owner");
        var category = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/catalog/categories", new { name = Unique("Bebidas"), parentId = (Guid?)null, sortOrder = 1, defaultMarginPercent = 30m, defaultMarkupPercent = (decimal?)null }));
        var categoryId = category.GetProperty("id").GetGuid();
        var input = NewProduct(Unique("Suco de uva"), await UnitAsync(owner));
        var json = JsonSerializer.SerializeToNode(input)!.AsObject();
        json["categoryId"] = categoryId;
        var product = await CreatedAsync(await owner.PostAsJsonAsync(Products, json));
        var productId = product.GetProperty("id").GetGuid();

        var deleteCategory = await owner.DeleteAsync($"/api/v1/catalog/categories/{categoryId}");
        deleteCategory.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(deleteCategory)).Should().Be("category_in_use");
        var general = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/catalog/categories")).EnumerateArray().Single(c => c.GetProperty("isDefault").GetBoolean());
        (await owner.DeleteAsync($"/api/v1/catalog/categories/{general.GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        Sold[productId] = true;
        var deleteProduct = await owner.DeleteAsync($"{Products}/{productId}");
        deleteProduct.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(deleteProduct)).Should().Be("product_in_use");
        (await owner.PostAsync($"{Products}/{productId}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PostAsync($"/api/v1/catalog/categories/{categoryId}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
