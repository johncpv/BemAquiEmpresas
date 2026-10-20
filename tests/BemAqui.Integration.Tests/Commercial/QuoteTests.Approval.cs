using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Aprovar_gera_contrato_projeto_orcado_e_eventos()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, customer) = await SentQuoteAsync(owner);

        // CA 10: quotes.edit sem quotes.approve.
        var (_, commercial) = await ActorAsync("commercial");
        (await commercial.GetAsync($"{Base}/{id}/approval")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await commercial.SendAsync(Post($"{Base}/{id}/approve", new { signedAt = Iso(Today), revenueAccountId = Guid.NewGuid(), version = Version(quote) }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CA 1 e 2: pré-condições atendidas, conta sugerida 1.1, nome do projeto.
        var preview = await JsonAsync(await owner.GetAsync($"{Base}/{id}/approval"));
        preview.GetProperty("preconditions").EnumerateArray().Should().OnlyContain(p => p.GetProperty("ok").GetBoolean());
        var account = preview.GetProperty("suggestedRevenueAccountId").GetGuid();
        (await WithDbAsync(db => db.Accounts.Where(a => a.Id == account).Select(a => a.Code).SingleAsync())).Should().Be("1.1");
        preview.GetProperty("projectName").GetString().Should().EndWith("— Elevador 6 paradas");
        preview.GetProperty("milestones").GetArrayLength().Should().Be(2);

        // CA 2: data no futuro e conta que não é de receita.
        var future = await owner.SendAsync(Post($"{Base}/{id}/approve", new { signedAt = Iso(Today.AddDays(1)), revenueAccountId = account, version = Version(quote) }));
        future.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await future.Content.ReadAsStringAsync()).Should().Contain("A data de assinatura não pode ser no futuro.");
        var expense = await WithDbAsync(db => db.Accounts.Where(a => a.Code == "2.1").Select(a => a.Id).SingleAsync());
        (await owner.SendAsync(Post($"{Base}/{id}/approve", new { signedAt = Iso(Today), revenueAccountId = expense, version = Version(quote) }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // CA 9: duas aprovações ao mesmo tempo, um contrato só.
        var signed = Today.AddDays(-3);
        var body = new { signedAt = Iso(signed), revenueAccountId = account, version = Version(quote) };
        var results = await Task.WhenAll(owner.SendAsync(Post($"{Base}/{id}/approve", body)), owner.SendAsync(Post($"{Base}/{id}/approve", body)));
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, string.Join(" | ", await Task.WhenAll(results.Select(r => r.Content.ReadAsStringAsync()))));
        var loser = results.Single(r => r.StatusCode != HttpStatusCode.Created);
        loser.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await loser.Content.ReadAsStringAsync()).Should().Contain("O orçamento já foi aprovado.");
        var approved = await JsonAsync(results.Single(r => r.StatusCode == HttpStatusCode.Created));
        var contractId = approved.GetProperty("contractId").GetGuid();
        var projectId = approved.GetProperty("projectId").GetGuid();
        approved.GetProperty("contractNumber").GetString().Should().StartWith("CTR-");
        approved.GetProperty("projectNumber").GetString().Should().StartWith("PRJ-");
        (await JsonAsync(await owner.GetAsync($"{Base}/{id}/contract"))).GetProperty("contractId").GetGuid().Should().Be(contractId);

        // CA 5 e 6: contrato, marcos e projeto.
        var after = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        after.GetProperty("status").GetString().Should().Be("Approved");
        var contract = await JsonAsync(await owner.GetAsync($"/api/v1/contracts/{contractId}"));
        contract.GetProperty("status").GetString().Should().Be("Active");
        contract.GetProperty("signedAt").GetString().Should().Be(Iso(signed));
        contract.GetProperty("totalValue").GetDecimal().Should().Be(after.GetProperty("totalPrice").GetDecimal());
        contract.GetProperty("revenueAccount").GetString().Should().StartWith("1.1");
        contract.GetProperty("milestones").EnumerateArray().Select(m => m.GetProperty("percent").GetDecimal()).Should().Equal(30m, 70m);
        contract.GetProperty("receivables").GetInt32().Should().Be(0);
        var project = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}"));
        project.GetProperty("status").GetString().Should().Be("Planned");
        project.GetProperty("promisedDate").GetString().Should().Be(Iso(signed.AddDays(90)));
        project.GetProperty("managerUserId").GetGuid().Should().Be(ownerId);
        project.GetProperty("priority").ValueKind.Should().Be(JsonValueKind.Null);
        project.GetProperty("items").GetArrayLength().Should().Be(1);

        // CA 8: orçado por natureza fecha no custo total.
        var totalCost = await WithDbAsync(db => db.Quotes.Where(q => q.Id == id).Select(q => q.TotalCost).SingleAsync());
        var budget = await JsonAsync(await owner.GetAsync($"/api/v1/projects/{projectId}/budget"));
        budget.EnumerateArray().Sum(b => b.GetProperty("budgetedAmount").GetDecimal()).Should().Be(totalCost);
        budget.EnumerateArray().Select(b => b.GetProperty("costType").GetString()).Should().BeEquivalentTo("Material", "Labor", "Freight");

        await EventsAndPrivacyAsync(owner, id, after, customer, contractId, projectId, account, contract);
    }
}
