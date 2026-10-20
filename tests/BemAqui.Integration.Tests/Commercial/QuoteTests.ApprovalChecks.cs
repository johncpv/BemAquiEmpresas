using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    private async Task EventsAndPrivacyAsync(HttpClient owner, Guid id, JsonElement after, Guid customer, Guid contractId, Guid projectId, Guid account, JsonElement contract)
    {
        // CA 5 e 7: eventos no outbox, com a conta de receita e sem dado pessoal.
        var outbox = (await WithDbAsync(db => db.OutboxMessages.AsNoTracking().ToListAsync())).Where(m => m.Payload.Contains(contractId.ToString()) || m.Payload.Contains(projectId.ToString())).ToList();
        outbox.Select(m => m.Type).Should().Contain(t => t.Contains("QuoteApproved")).And.Contain(t => t.Contains("ContractSigned")).And.Contain(t => t.Contains("ProjectItemCreated"));
        outbox.Single(m => m.Type.Contains("ContractSigned")).Payload.Should().Contain(account.ToString()).And.NotContain(Cpf);

        // CA 12 e 13: snapshot sem dado pessoal; a auditoria registra a criação sem o JSON e sem o CPF.
        var snapshot = await WithDbAsync(db => db.Contracts.Where(c => c.Id == contractId).Select(c => c.Snapshot).SingleAsync());
        snapshot.Should().Contain("\"schema\": \"v1\"").And.NotContain(Cpf);
        var audit = await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == "Contract" && a.EntityId == contractId.ToString()).ToListAsync());
        audit.Should().ContainSingle(a => a.Action == "Created");
        audit[0].Changes.Should().NotContain("Elevador 6 paradas").And.NotContain(Cpf);
        (await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity != "Party").ToListAsync())).Should().NotContain(a => a.Changes.Contains(Cpf));

        // CA 12: sem projects.view_cost, o snapshot vem sem custo e o orçado é negado.
        var (_, engineering) = await ActorAsync("engineering");
        var plain = await JsonAsync(await engineering.GetAsync($"/api/v1/contracts/{contractId}"));
        plain.GetProperty("snapshot").GetProperty("totalCost").ValueKind.Should().Be(JsonValueKind.Null);
        plain.GetProperty("snapshot").GetProperty("items")[0].GetProperty("costs").ValueKind.Should().Be(JsonValueKind.Null);
        (await engineering.GetAsync($"/api/v1/projects/{projectId}/budget")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        contract.GetProperty("snapshot").GetProperty("items")[0].GetProperty("costs").GetArrayLength().Should().Be(3);

        // CA 14: aprovado não muda.
        const string Locked = "Orçamento aprovado não é alterado. Ajustes no contrato são feitos por aditivo.";
        var version = Version(after);
        foreach (var attempt in new Func<Task<HttpResponseMessage>>[]
        {
            () => owner.PutAsJsonAsync($"{Base}/{id}", new { contactId = (Guid?)null, siteAddressId = (Guid?)null, deliveryDays = 10, notes = "x", version }),
            () => owner.SendAsync(Post($"{Base}/{id}/revisions", new { version })),
            () => owner.PostAsJsonAsync($"{Base}/{id}/extend-validity", new { validUntil = Iso(Today.AddDays(30)), version }),
            () => owner.PostAsJsonAsync($"{Base}/{id}/lose", new { reason = "Price", detail = (string?)null, version }),
            () => owner.PostAsJsonAsync($"{Base}/{id}/send", new { version }),
        })
        {
            var response = await attempt();
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await response.Content.ReadAsStringAsync()).Should().Contain(Locked);
        }

        // CA 16 e 20: lista de contratos; cliente com contrato não é excluído; IDOR.
        var list = await JsonAsync(await owner.GetAsync("/api/v1/contracts"));
        list.GetProperty("items").EnumerateArray().Should().Contain(c => c.GetProperty("id").GetGuid() == contractId);
        (await (await owner.DeleteAsync($"/api/v1/parties/{customer}")).Content.ReadAsStringAsync()).Should().Contain("contratos");
        (await owner.GetAsync($"/api/v1/contracts/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetAsync($"/api/v1/projects/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Aprovacao_recusa_pre_condicoes_sem_gravar()
    {
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var customer = await CustomerAsync(owner);
        var quote = await JsonAsync(await owner.SendAsync(Post(Base, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/items", Item("Plataforma", 1m, Version(quote), price: 1_000m)));
        await owner.PutAsJsonAsync($"{Base}/{id}/conditions", new { milestones = new[] { Milestone("Sinal", 90m) }, validUntil = Iso(Today.AddDays(15)), version = Version(quote) });
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        quote = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/send", new { version = Version(quote) }));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.quotes SET valid_until = {Today.AddDays(-1)} WHERE id = {id}"));
        quote = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));

        // CA 1 e 4: as que falham aparecem com o motivo; a aprovação responde 422 e nada é gravado.
        var preview = await JsonAsync(await owner.GetAsync($"{Base}/{id}/approval"));
        preview.GetProperty("preconditions").EnumerateArray().Where(p => !p.GetProperty("ok").GetBoolean()).Select(p => p.GetProperty("text").GetString())
            .Should().Contain(t => t!.StartsWith("A validade venceu em")).And.Contain("Os marcos somam 90%. Precisam somar 100%.");
        var account = preview.GetProperty("suggestedRevenueAccountId").GetGuid();
        var refused = await owner.SendAsync(Post($"{Base}/{id}/approve", new { signedAt = Iso(Today), revenueAccountId = account, version = Version(quote) }));
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("Precisam somar 100%");
        (await WithDbAsync(db => db.Contracts.CountAsync(c => c.QuoteId == id))).Should().Be(0);
        (await JsonAsync(await owner.GetAsync($"{Base}/{id}"))).GetProperty("status").GetString().Should().Be("Sent");
    }
}
