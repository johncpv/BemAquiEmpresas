using System.Net;
using System.Text.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

public partial class CashFlowTests
{
    private static Dictionary<DateOnly, (decimal In, decimal Out)> Expected()
    {
        var map = new Dictionary<DateOnly, (decimal In, decimal Out)>();
        void Add(int day, decimal inflow, decimal outflow) => Add2(Today.AddDays(day), inflow, outflow);
        void Add2(DateOnly d, decimal inflow, decimal outflow) { var t = map.GetValueOrDefault(d); map[d] = (t.In + inflow, t.Out + outflow); }
        Add(-5, 1_000m, 0); Add(-3, 0, 500m); Add(-1, 0, 300m);
        Add(0, 200m + 2_000m, 3_000m); // realizado de hoje + vencidos em aberto (R2 e P2)
        Add(5, 1_000m + 700m, 950m + 150m); // R1, R5 (implantação); P1 líquido do ISS, P4 pelo saldo
        Add(10, 1_000m, 0); // R3 pelo saldo
        Add(15, 0, 1_200m); // P5 estimado
        Add(20, 0, 500m);
        Add(40, 800m, 0);
        for (var d = NextTenth(); d <= Today.AddDays(90); d = d.AddMonths(1))
        {
            Add2(d, 0, 3_000m); // o título já gerado da recorrente e as competências seguintes, sem duplicar
        }

        return map;
    }

    [Fact]
    public async Task Fluxo_diario_semanal_e_mensal_com_o_dataset_fixo()
    {
        var data = await SeedAsync();
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var from = Today.AddDays(-7);
        var response = await owner.GetAsync($"{Base}?view=daily&days=90&from={Iso(from)}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var flow = await JsonAsync(response);
        flow.GetProperty("currentBalance").GetDecimal().Should().Be(12_500m, "saldo inicial + transações até hoje, sem a futura (CA 2)");

        var expected = Expected();
        var balance = 12_100m;
        var periods = flow.GetProperty("periods").EnumerateArray().ToList();
        periods.Should().HaveCount(98);
        var lowest = (Date: DateOnly.MaxValue, Balance: decimal.MaxValue);
        foreach (var p in periods)
        {
            var day = DateOnly.Parse(p.GetProperty("start").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
            var (inflow, outflow) = expected.GetValueOrDefault(day);
            balance += inflow - outflow;
            p.GetProperty("inflow").GetDecimal().Should().Be(inflow, Iso(day));
            p.GetProperty("outflow").GetDecimal().Should().Be(outflow, Iso(day));
            p.GetProperty("balance").GetDecimal().Should().Be(balance, Iso(day));
            p.GetProperty("projected").GetBoolean().Should().Be(day >= Today);
            if (day >= Today && balance < lowest.Balance)
            {
                lowest = (day, balance);
            }
        }

        var today = periods.Single(p => p.GetProperty("containsToday").GetBoolean());
        today.GetProperty("overdueInflow").GetDecimal().Should().Be(2_000m);
        today.GetProperty("overdueOutflow").GetDecimal().Should().Be(3_000m);
        flow.GetProperty("lowest").GetProperty("date").GetString().Should().Be(Iso(lowest.Date));
        flow.GetProperty("lowest").GetProperty("balance").GetDecimal().Should().Be(lowest.Balance);
        flow.GetProperty("undatedReceivables").GetProperty("total").GetDecimal().Should().Be(4_000m, "marco sem data fica fora da série (CA 6)");

        foreach (var view in new[] { "weekly", "monthly" })
        {
            var grouped = (await JsonAsync(await owner.GetAsync($"{Base}?view={view}&days=90&from={Iso(from)}"))).GetProperty("periods").EnumerateArray().ToList();
            grouped.Sum(p => p.GetProperty("inflow").GetDecimal()).Should().Be(periods.Sum(p => p.GetProperty("inflow").GetDecimal()), view);
            grouped.Sum(p => p.GetProperty("outflow").GetDecimal()).Should().Be(periods.Sum(p => p.GetProperty("outflow").GetDecimal()), view);
            grouped[^1].GetProperty("balance").GetDecimal().Should().Be(balance, view);
        }

        await DayAndAccountAsync(owner, data);
    }

    private static async Task DayAndAccountAsync(HttpClient owner, Dataset data)
    {
        var today = (await JsonAsync(await owner.GetAsync($"{Base}/day?date={Iso(Today)}"))).GetProperty("items").EnumerateArray().ToList();
        today.Should().Contain(i => i.GetProperty("kind").GetString() == "Transaction" && i.GetProperty("amount").GetDecimal() == 200m);
        var overdue = today.Single(i => i.GetProperty("id").GetGuid() == data.Overdue);
        overdue.GetProperty("overdue").GetBoolean().Should().BeTrue();
        overdue.GetProperty("link").GetString().Should().Be("/app/financeiro/receber/" + data.Overdue);
        var estimate = (await JsonAsync(await owner.GetAsync($"{Base}/day?date={Iso(Today.AddDays(15))}&sort=-amount"))).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == data.Estimate);
        estimate.GetProperty("estimate").GetBoolean().Should().BeTrue("CA 5");
        estimate.GetProperty("amount").GetDecimal().Should().Be(-1_200m);
        var recurring = (await JsonAsync(await owner.GetAsync($"{Base}/day?date={Iso(NextTenth().AddMonths(1))}"))).GetProperty("items").EnumerateArray().ToList();
        recurring.Should().ContainSingle(i => i.GetProperty("kind").GetString() == "Recurring" && i.GetProperty("amount").GetDecimal() == -3_000m);
        (await JsonAsync(await owner.GetAsync($"{Base}/day?date={Iso(NextTenth())}"))).GetProperty("items").EnumerateArray()
            .Count(i => i.GetProperty("amount").GetDecimal() == -3_000m).Should().Be(1, "o título gerado não duplica com a recorrente (CA 4)");

        // CA 9: por conta, só o realizado e o saldo da conta.
        var account = await JsonAsync(await owner.GetAsync($"{Base}?bankAccountId={data.BankA}&from={Iso(Today.AddDays(-7))}"));
        account.GetProperty("consolidated").GetBoolean().Should().BeFalse();
        account.GetProperty("currentBalance").GetDecimal().Should().Be(10_700m);
        account.GetProperty("periods").EnumerateArray().Should().OnlyContain(p => !p.GetProperty("projected").GetBoolean());
        account.GetProperty("lowest").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Filtros_fora_da_allowlist_respondem_422_e_sem_grupo_403()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        (await owner.GetAsync($"{Base}/day?date={Iso(Today)}&sort=amount;drop")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync($"{Base}?view=yearly")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync($"{Base}?days=400")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.GetAsync($"{Base}?bankAccountId={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var nobody = await ActorAsync(null);
        var denied = await nobody.GetAsync(Base);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(denied)).GetProperty("code").GetString().Should().Be("forbidden");
    }
}
