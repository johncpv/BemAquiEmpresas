using System.Net;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Inadimplencia_por_projeto_e_comercial_financeiro_do_terceiro()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, customer) = await SentQuoteAsync(owner, canary: false);
        var (contractId, projectId) = await ApproveAsync(owner, id, quote, Today.AddDays(-40));
        var sinal = (await ParcelsAsync(contractId)).Single(p => p.Description.Contains("Sinal"));

        // CA 1: o sinal vencido há 40 dias cai na faixa 31–60 do projeto.
        var report = await JsonAsync(await owner.GetAsync("/api/v1/finance/aging/projects"));
        var row = report.GetProperty("rows").EnumerateArray().Single(r => r.ValueKind != System.Text.Json.JsonValueKind.Null && r.GetProperty("projectId").ValueKind != System.Text.Json.JsonValueKind.Null && r.GetProperty("projectId").GetGuid() == projectId);
        row.GetProperty("buckets")[1].GetDecimal().Should().Be(sinal.Amount);
        row.GetProperty("projectNumber").GetString().Should().StartWith("PRJ-");

        // CA 2 a 5: depois da baixa atrasada, a pontualidade e os totais do terceiro.
        var bank = await SettleBankAsync(owner);
        await SettleAsync(owner, bank, sinal.Id);
        var summaryResponse = await owner.GetAsync($"/api/v1/finance/parties/{customer}/summary");
        summaryResponse.Headers.CacheControl!.NoStore.Should().BeTrue("CA 7");
        var summary = await JsonAsync(summaryResponse);
        summary.GetProperty("paymentStats").GetProperty("hasHistory").GetBoolean().Should().BeTrue();
        summary.GetProperty("paymentStats").GetProperty("avgDelayDays").GetDecimal().Should().Be(40m);
        summary.GetProperty("paymentStats").GetProperty("pctPaidLate").GetDecimal().Should().Be(100m);
        summary.GetProperty("receivables").GetProperty("paidLast12m").GetDecimal().Should().Be(sinal.Amount);
        var aging = await JsonAsync(await owner.GetAsync("/api/v1/finance/aging"));
        aging.GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("partyId").GetGuid() == customer).Should().BeEmpty("quitou o vencido");
        (await JsonAsync(await owner.GetAsync($"/api/v1/quotes?partyId={customer}"))).GetProperty("items").GetArrayLength().Should().Be(1);

        // CA 6: sem finance.view, 403.
        var (_, engineering) = await ActorAsync("engineering");
        (await engineering.GetAsync($"/api/v1/finance/parties/{customer}/summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await engineering.GetAsync("/api/v1/finance/aging/projects")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _ = await WithDbAsync(db => db.Receivables.CountAsync());
    }
}
