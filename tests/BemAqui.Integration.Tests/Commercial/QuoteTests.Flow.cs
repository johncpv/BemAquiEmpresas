using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Orcamento_com_composicao_preco_pelo_markup_preco_digitado_e_envio()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, commercial) = await ActorAsync("commercial");
        (await owner.PutAsJsonAsync("/api/v1/commercial/parameters", new { defaultPct = 40m })).StatusCode.Should().Be(HttpStatusCode.OK);
        var customer = await CustomerAsync(owner);
        var supplier = await CustomerAsync(owner, "Supplier");
        var (stock, position) = await CatalogAsync();

        // CA 4 e 5: só cliente; nasce rascunho com número ORC.
        (await commercial.SendAsync(Post(Base, new { partyId = supplier, contactId = (Guid?)null, siteAddressId = (Guid?)null }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var created = await commercial.SendAsync(Post(Base, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null }));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var quote = await JsonAsync(created);
        var id = quote.GetProperty("id").GetGuid();
        quote.GetProperty("number").GetString().Should().StartWith("ORC-");
        quote.GetProperty("status").GetString().Should().Be("Draft");

        // CA 7 e 8: item e composição por natureza; custo unitário = soma das linhas; preço pelo markup padrão.
        quote = await JsonAsync(await commercial.PostAsJsonAsync($"{Base}/{id}/items", Item("Elevador 6 paradas", 2m, Version(quote))));
        var itemId = quote.GetProperty("items")[0].GetProperty("id").GetGuid();
        var costs = new
        {
            lines = new object[]
            {
                new { costType = "Material", description = (string?)null, itemId = stock, workCenterId = (Guid?)null, positionId = (Guid?)null, partyId = (Guid?)null, quantity = 10m, unitCost = 12.5m },
                new { costType = "Labor", description = (string?)null, itemId = (Guid?)null, workCenterId = (Guid?)null, positionId = position, partyId = (Guid?)null, quantity = 8m, unitCost = (decimal?)999m },
                new { costType = "Freight", description = (string?)null, itemId = (Guid?)null, workCenterId = (Guid?)null, positionId = (Guid?)null, partyId = (Guid?)null, quantity = 1m, unitCost = 75m },
            },
            pct = (decimal?)null, version = Version(quote),
        };
        var set = await commercial.PutAsJsonAsync($"{Base}/{id}/items/{itemId}/costs", costs);
        set.StatusCode.Should().Be(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
        var detail = await JsonAsync(set);
        var costItem = detail.GetProperty("items")[0];
        costItem.GetProperty("unitCost").GetDecimal().Should().Be(600m, "125 + 8 h × R$ 50,00 do cargo (nunca o valor enviado) + 75");
        costItem.GetProperty("suggestedPrice").GetDecimal().Should().Be(840m);
        detail.GetProperty("totalCost").GetDecimal().Should().Be(1_200m);
        detail.GetProperty("totalPrice").GetDecimal().Should().Be(1_680m);
        detail.GetProperty("marginPct").GetDecimal().Should().Be(28.5714m);
        (await JsonAsync(await commercial.GetAsync($"{Base}/material-cost/{stock}"))).GetProperty("unitCost").GetDecimal().Should().Be(12.5m, "CA 11");

        // CA 14: preço digitado vale e o % é recalculado.
        quote = await JsonAsync(await commercial.GetAsync($"{Base}/{id}"));
        quote = await JsonAsync(await commercial.PutAsJsonAsync($"{Base}/{id}/items/{itemId}", Item("Elevador 6 paradas", 2m, Version(quote), price: 800m)));
        quote.GetProperty("items")[0].GetProperty("priceOverridden").GetBoolean().Should().BeTrue();
        var afterOverride = await JsonAsync(await commercial.GetAsync($"{Base}/{id}/costs"));
        afterOverride.GetProperty("items")[0].GetProperty("effectivePct").GetDecimal().Should().Be(33.3333m);

        // CA 13: custo-hora mudou; o preview mostra a diferença e "Atualizar custos" grava sem mexer no preço digitado.
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE hr.job_positions SET hourly_cost = 60 WHERE id = {position}"));
        var preview = await JsonAsync(await commercial.GetAsync($"{Base}/{id}/refresh-costs"));
        preview.GetProperty("lines")[0].GetProperty("newUnitCost").GetDecimal().Should().Be(60m);
        preview.GetProperty("newTotalCost").GetDecimal().Should().Be(1_360m);
        var refreshed = await JsonAsync(await commercial.PostAsJsonAsync($"{Base}/{id}/refresh-costs", new { version = Version(quote) }));
        refreshed.GetProperty("items")[0].GetProperty("unitCost").GetDecimal().Should().Be(680m);
        refreshed.GetProperty("totalPrice").GetDecimal().Should().Be(1_600m, "o preço digitado não muda");

        // CA 16 e 17: enviado fica só leitura.
        quote = await JsonAsync(await commercial.GetAsync($"{Base}/{id}"));
        var sent = await JsonAsync(await commercial.PostAsJsonAsync($"{Base}/{id}/send", new { version = Version(quote) }));
        sent.GetProperty("status").GetString().Should().Be("Sent");
        var locked = await commercial.PutAsJsonAsync($"{Base}/{id}/items/{itemId}", Item("Outro", 1m, Version(sent)));
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(locked)).Should().Be("quote_sent");
        (await owner.DeleteAsync($"{Base}/{id}?version={Version(sent)}")).StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 18");
    }
}
