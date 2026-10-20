using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    private static object Milestone(string description, decimal percent, string trigger = "Event", string? triggerEvent = "Signature", string? dueDate = null, int? days = 0) =>
        new { description, percent, trigger, triggerEvent, dueDate, daysAfterEvent = days };

    [Fact]
    public async Task Marcos_do_PRD_validade_perda_e_copia()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var customer = await CustomerAsync(owner);
        var quote = await JsonAsync(await owner.SendAsync(Post(Base, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/items", Item("Elevador", 1m, Version(quote), price: 100_000m)));

        // CA 8: validade padrão de 15 dias; no passado, 422.
        var conditions = await JsonAsync(await owner.GetAsync($"{Base}/{id}/conditions"));
        conditions.GetProperty("validUntil").GetString().Should().Be(Iso(Today.AddDays(15)));
        object Body(object[] milestones, DateOnly? valid = null) => new { milestones, validUntil = Iso(valid ?? Today.AddDays(15)), version = Version(quote) };
        (await owner.PutAsJsonAsync($"{Base}/{id}/conditions", Body([], Today.AddDays(-1)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // CA 7: % fora do intervalo, 422 na linha.
        var bad = await owner.PutAsJsonAsync($"{Base}/{id}/conditions", Body([Milestone("Sinal", 0m)]));
        (await bad.Content.ReadAsStringAsync()).Should().Contain("milestones[0].percent");

        // CA 3: exemplo do PRD.
        var saved = await owner.PutAsJsonAsync($"{Base}/{id}/conditions", Body([
            Milestone("Sinal", 30m), Milestone("Fabricação concluída", 40m, triggerEvent: "FabricationDone", days: 10), Milestone("Entrega técnica", 30m, triggerEvent: "TechnicalAcceptance", days: 10)]));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        conditions = await JsonAsync(saved);
        conditions.GetProperty("milestones").EnumerateArray().Select(m => m.GetProperty("amount").GetDecimal()).Should().Equal(30_000m, 40_000m, 30_000m);
        conditions.GetProperty("warning").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        // CA 6: o preço mudou, os valores seguem os percentuais.
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        var itemId = quote.GetProperty("items")[0].GetProperty("id").GetGuid();
        quote = await JsonAsync(await owner.PutAsJsonAsync($"{Base}/{id}/items/{itemId}", Item("Elevador", 1m, Version(quote), price: 50_000m)));
        (await JsonAsync(await owner.GetAsync($"{Base}/{id}/conditions"))).GetProperty("milestones").EnumerateArray().Select(m => m.GetProperty("amount").GetDecimal()).Should().Equal(15_000m, 20_000m, 15_000m);

        // CA 4: soma diferente de 100% grava como rascunho com aviso.
        var partial = await JsonAsync(await owner.PutAsJsonAsync($"{Base}/{id}/conditions", Body([Milestone("Sinal", 90m)])));
        partial.GetProperty("warning").GetString().Should().Be("A soma dos marcos está em 90%. Para aprovar, precisa ser 100%.");

        // CA 13: cópia com itens, composição e marcos; o original não muda.
        var copy = await owner.SendAsync(Post($"{Base}/{id}/copy", new { partyId = (Guid?)null, contactId = (Guid?)null, siteAddressId = (Guid?)null }));
        copy.StatusCode.Should().Be(HttpStatusCode.Created, await copy.Content.ReadAsStringAsync());
        var copied = await JsonAsync(copy);
        copied.GetProperty("number").GetString().Should().NotBe(quote.GetProperty("number").GetString());
        copied.GetProperty("totalPrice").GetDecimal().Should().Be(50_000m);
        (await JsonAsync(await owner.GetAsync($"{Base}/{copied.GetProperty("id").GetGuid()}/conditions"))).GetProperty("milestones").GetArrayLength().Should().Be(1);

        // CA 9, 11 e 12: validade vencida não envia; perda só do enviado e depois só leitura.
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.quotes SET valid_until = {Today.AddDays(-1)} WHERE id = {id}"));
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        (await (await owner.PostAsJsonAsync($"{Base}/{id}/send", new { version = Version(quote) })).Content.ReadAsStringAsync()).Should().Contain("A validade venceu");
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.quotes SET valid_until = {Today.AddDays(5)} WHERE id = {id}"));
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/send", new { version = Version(quote) }));
        (await owner.PostAsJsonAsync($"{Base}/{id}/lose", new { reason = (string?)null, detail = (string?)null, version = Version(quote) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var lost = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/lose", new { reason = "Price", detail = "Concorrente 10% mais barato", version = Version(quote) }));
        lost.GetProperty("status").GetString().Should().Be("Rejected");
        var locked = await owner.PutAsJsonAsync($"{Base}/{id}/items/{itemId}", Item("x", 1m, Version(lost)));
        (await locked.Content.ReadAsStringAsync()).Should().Contain("Orçamento perdido não é alterado.");
    }
}
