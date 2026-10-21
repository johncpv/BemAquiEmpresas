using System.Net;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Funil_por_situacao_conversao_motivos_autor_e_vencendo()
    {
        var (authorId, author) = await ActorAsync(Group.OwnerSeedKey);
        var (otherId, _) = await ActorAsync(Group.OwnerSeedKey);
        var customer = await CustomerAsync(author);
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var inPeriod = DateTimeOffset.UtcNow.AddDays(-10);

        // Dataset fixo (CA 1): só a revisão corrente conta; o número revisado entra uma vez, pela revisão 0 do autor.
        await WithDbAsync(async db =>
        {
            Quote Q(string suffix, QuoteStatus status, decimal price, DateTimeOffset? createdAt = null, Guid? by = null, int revision = 0, bool current = true,
                QuoteLostReason? reason = null, string? detail = null, DateOnly? validUntil = null) => new()
            {
                Number = $"FUN-{tag}-{suffix}", Revision = revision, IsCurrent = current, Status = status, PartyId = customer, TotalPrice = price, TotalCost = price / 2,
                CreatedAt = createdAt ?? inPeriod, CreatedBy = by ?? authorId, LostReason = reason, LostDetail = detail, LostAt = reason is null ? null : inPeriod,
                ValidUntil = validUntil,
            };

            db.Quotes.AddRange(
                Q("D1", QuoteStatus.Draft, 100m), Q("D2", QuoteStatus.Draft, 200m),
                Q("S1", QuoteStatus.Sent, 1_000m, validUntil: Today.AddDays(3)),
                Q("A1", QuoteStatus.Approved, 3_000m), Q("A2", QuoteStatus.Approved, 1_000m),
                Q("L1", QuoteStatus.Rejected, 500m, reason: QuoteLostReason.Price), Q("L2", QuoteStatus.Rejected, 700m, reason: QuoteLostReason.Price),
                Q("L3", QuoteStatus.Rejected, 300m, reason: QuoteLostReason.Other, detail: "Cliente mudou o projeto"),
                Q("E1", QuoteStatus.Expired, 1_000m),
                Q("R", QuoteStatus.Superseded, 350m, current: false),
                Q("R", QuoteStatus.Sent, 400m, createdAt: DateTimeOffset.UtcNow, by: otherId, revision: 1, validUntil: Today.AddDays(20)),
                Q("OLD", QuoteStatus.Approved, 5_000m, createdAt: DateTimeOffset.UtcNow.AddDays(-200)),
                Q("B1", QuoteStatus.Approved, 9_000m, by: otherId));
            await db.SaveChangesAsync();
            return true;
        });

        var url = $"{Base}/funnel?authorId={authorId}";
        var response = await author.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var funnel = await JsonAsync(response);
        funnel.GetProperty("from").GetString().Should().Be(Iso(Today.AddDays(-90)), "padrão: últimos 90 dias");
        var stages = funnel.GetProperty("stages").EnumerateArray().ToDictionary(s => s.GetProperty("status").GetString()!, s => (Count: s.GetProperty("count").GetInt32(), Amount: s.GetProperty("amount").GetDecimal()));
        stages["Draft"].Should().Be((2, 300m));
        stages["Sent"].Should().Be((2, 1_400m), "a revisão corrente do número revisado conta como enviada, uma vez só");
        stages["Approved"].Should().Be((2, 4_000m), "o de 200 dias atrás e o do outro autor ficam fora");
        stages["Rejected"].Should().Be((3, 1_500m));
        stages["Expired"].Should().Be((1, 1_000m));

        // CA 2: aprovados ÷ decididos, uma casa.
        var conversion = funnel.GetProperty("conversion");
        conversion.GetProperty("byCount").GetDecimal().Should().Be(33.3m);
        conversion.GetProperty("byAmount").GetDecimal().Should().Be(61.5m);
        conversion.GetProperty("decided").GetInt32().Should().Be(6);

        // CA 3: motivos do maior para o menor, com % e o detalhe dos "Outro".
        var reasons = funnel.GetProperty("lossReasons").EnumerateArray().ToList();
        reasons.Select(r => r.GetProperty("reason").GetString()).Should().Equal("Price", "Other");
        reasons[0].GetProperty("amount").GetDecimal().Should().Be(1_200m);
        reasons[0].GetProperty("pct").GetDecimal().Should().Be(66.7m);
        funnel.GetProperty("otherLosses").EnumerateArray().Single().GetProperty("detail").GetString().Should().Be("Cliente mudou o projeto");

        // CA 6: vencendo em até 7 dias.
        var expiring = funnel.GetProperty("expiring").EnumerateArray().ToList();
        expiring.Should().ContainSingle().Which.GetProperty("number").GetString().Should().Be($"FUN-{tag}-S1");
        expiring[0].GetProperty("daysLeft").GetInt32().Should().Be(3);

        // CA 4: o autor está no filtro; sem filtro, o do outro autor entra.
        funnel.GetProperty("authors").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).Should().Contain(authorId);
        var all = await JsonAsync(await author.GetAsync($"{Base}/funnel"));
        all.GetProperty("stages").EnumerateArray().Single(s => s.GetProperty("status").GetString() == "Approved").GetProperty("count").GetInt32().Should().BeGreaterThanOrEqualTo(3);

        // CA 8: só preço, nunca custo ou margem; ordenação fora da allowlist é 422.
        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContainEquivalentOf("cost").And.NotContainEquivalentOf("margin");
        (await author.GetAsync($"{url}&sort=totalCost")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await author.GetAsync($"{url}&sort=-totalPrice")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await author.GetAsync($"{Base}/funnel?from={Iso(Today)}&to={Iso(Today.AddDays(-1))}")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // CA 5: drill-down na lista, pela situação, pelo período e pelo autor.
        var list = await JsonAsync(await author.GetAsync($"{Base}?situation=aprovado&from={Iso(Today.AddDays(-90))}&to={Iso(Today)}&authorId={authorId}"));
        list.GetProperty("total").GetInt32().Should().Be(2);
        list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("number").GetString()).Should().BeEquivalentTo($"FUN-{tag}-A1", $"FUN-{tag}-A2");

        // CA 7: sem grupo, 403 forbidden.
        var (_, nobody) = await ActorAsync(null);
        var forbidden = await nobody.GetAsync($"{Base}/funnel");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(forbidden)).Should().Be("forbidden");
    }

    [Fact]
    public void Conversao_sem_decididos_e_traco()
    {
        static FunnelStage S(QuoteStatus status, int count, decimal amount) => new(status, string.Empty, count, amount);
        var none = QuoteService.Conversion(S(QuoteStatus.Approved, 0, 0), S(QuoteStatus.Rejected, 0, 0), S(QuoteStatus.Expired, 0, 0));
        none.ByCount.Should().BeNull();
        none.ByAmount.Should().BeNull();
        var some = QuoteService.Conversion(S(QuoteStatus.Approved, 1, 1_000m), S(QuoteStatus.Rejected, 2, 500m), S(QuoteStatus.Expired, 0, 0));
        some.ByCount.Should().Be(33.3m);
        some.ByAmount.Should().Be(66.7m);
        some.Decided.Should().Be(3);
    }
}
