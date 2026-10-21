using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Aditivo_de_acrescimo_gera_parcelas_e_soma_no_orcado_e_no_prazo()
    {
        var (_, author) = await ActorAsync(Group.OwnerSeedKey);
        var (_, approver) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, _) = await SentQuoteAsync(author, canary: false);
        var (contractId, projectId) = await ApproveAsync(author, id, quote, Today);
        var before = await JsonAsync(await author.GetAsync($"/api/v1/projects/{projectId}"));
        var budgetBefore = await WithDbAsync(db => db.ProjectBudgets.Where(b => b.ProjectId == projectId).SumAsync(b => b.BudgetedAmount));
        var path = $"/api/v1/contracts/{contractId}/amendments";

        // CA 1, 3 e 4: marcos precisam somar 100%; quem só vê não registra.
        object Body(object[] milestones) => new { description = "Mais uma parada", valueDelta = 10_000m, daysDelta = 15, milestones, reductions = (object[]?)null, budgetDeltas = (object?)null, version = (uint?)null };
        (await author.SendAsync(Post(path, Body([Milestone("Sinal", 40m)])))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var (_, commercial) = await ActorAsync("commercial");
        (await commercial.SendAsync(Post(path, Body([Milestone("Sinal", 100m)])))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var created = await author.SendAsync(Post(path, Body([Milestone("Sinal do aditivo", 50m), Milestone("Entrega do aditivo", 50m, triggerEvent: "Delivered", days: 10)])));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var amendment = await JsonAsync(created);
        var amendmentId = amendment.GetProperty("id").GetGuid();
        amendment.GetProperty("number").GetInt32().Should().Be(1);
        amendment.GetProperty("milestones").EnumerateArray().Select(m => m.GetProperty("amount").GetDecimal()).Should().Equal(5_000m, 5_000m);

        // CA 5 e 9: enviado; o autor não aprova (há outros Proprietários e o parâmetro está desligado).
        amendment = await JsonAsync(await author.PostAsJsonAsync($"{path}/{amendmentId}/submit", new { version = Version(amendment) }));
        amendment.GetProperty("status").GetString().Should().Be("PendingApproval");
        var self = await author.PostAsJsonAsync($"{path}/{amendmentId}/approve", new { version = Version(amendment) });
        self.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await self.Content.ReadAsStringAsync()).Should().Contain("Quem registrou o aditivo não pode aprová-lo.");

        // CA 6 e 9: aprovação dupla grava uma vez.
        var results = await Task.WhenAll(approver.PostAsJsonAsync($"{path}/{amendmentId}/approve", new { version = Version(amendment) }), approver.PostAsJsonAsync($"{path}/{amendmentId}/approve", new { version = Version(amendment) }));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1, string.Join(" | ", await Task.WhenAll(results.Select(r => r.Content.ReadAsStringAsync()))));
        await DrainAsync();

        // CA 6, 7 e 10: orçado e prazo somados, parcelas novas, valor vigente.
        (await WithDbAsync(db => db.ProjectBudgets.Where(b => b.ProjectId == projectId).SumAsync(b => b.BudgetedAmount))).Should().Be(budgetBefore + 10_000m);
        var promised = DateOnly.Parse(before.GetProperty("promisedDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        (await JsonAsync(await author.GetAsync($"/api/v1/projects/{projectId}"))).GetProperty("promisedDate").GetString().Should().Be(Iso(promised.AddDays(15)));
        var parcels = await ParcelsAsync(contractId);
        parcels.Should().HaveCount(4);
        parcels.Single(p => p.Description.Contains("Sinal do aditivo")).Status.Should().Be(ReceivableStatus.Open);
        parcels.Single(p => p.Description.Contains("Entrega do aditivo")).Status.Should().Be(ReceivableStatus.Planned);
        var contract = await JsonAsync(await author.GetAsync($"/api/v1/contracts/{contractId}"));
        contract.GetProperty("currentValue").GetDecimal().Should().Be(contract.GetProperty("totalValue").GetDecimal() + 10_000m);
        (await JsonAsync(await author.GetAsync(path))).GetArrayLength().Should().Be(1);
        (await author.GetAsync($"{path}/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR (CA 11)");
    }
}
