using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    private const string Cpf = "52998224725";

    /// <summary>Orçamento enviado de cliente PF (CPF canário), com composição de 3 naturezas e marcos 30/70.</summary>
    private async Task<(Guid Id, JsonElement Quote, Guid Customer)> SentQuoteAsync(HttpClient owner)
    {
        var (stock, position) = await CatalogAsync();
        var party = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PF", document = Cpf, legalName = "Ana Canário " + Guid.NewGuid().ToString("N")[..5], roles = new[] { "Customer" } });
        party.StatusCode.Should().Be(HttpStatusCode.Created, await party.Content.ReadAsStringAsync());
        var customer = (await JsonAsync(party)).GetProperty("id").GetGuid();
        var quote = await JsonAsync(await owner.SendAsync(Post(Base, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.quotes SET delivery_days = 90 WHERE id = {id}"));
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/items", Item("Elevador 6 paradas", 3m, Version(quote))));
        var itemId = quote.GetProperty("items")[0].GetProperty("id").GetGuid();
        var lines = new object[]
        {
            new { costType = "Material", description = (string?)null, itemId = stock, workCenterId = (Guid?)null, positionId = (Guid?)null, partyId = (Guid?)null, quantity = 3m, unitCost = 33.333333m },
            new { costType = "Labor", description = (string?)null, itemId = (Guid?)null, workCenterId = (Guid?)null, positionId = position, partyId = (Guid?)null, quantity = 0.3333m, unitCost = (decimal?)null },
            new { costType = "Freight", description = (string?)null, itemId = (Guid?)null, workCenterId = (Guid?)null, positionId = (Guid?)null, partyId = (Guid?)null, quantity = 1m, unitCost = 10.01m },
        };
        var set = await owner.PutAsJsonAsync($"{Base}/{id}/items/{itemId}/costs", new { lines, pct = (decimal?)40m, version = Version(quote) });
        set.StatusCode.Should().Be(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        var conditions = await owner.PutAsJsonAsync($"{Base}/{id}/conditions", new
        {
            milestones = new[] { Milestone("Sinal", 30m), Milestone("Entrega", 70m, triggerEvent: "Delivered", days: 10) }, validUntil = Iso(Today.AddDays(15)), version = Version(quote),
        });
        conditions.StatusCode.Should().Be(HttpStatusCode.OK, await conditions.Content.ReadAsStringAsync());
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/send", new { version = Version(quote) }));
        quote.GetProperty("status").GetString().Should().Be("Sent");
        return (id, quote, customer);
    }
}
