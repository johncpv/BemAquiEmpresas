using System.Globalization;
using System.Net;
using System.Text.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

public partial class AdjustedCashFlowTests
{
    private static DateOnly Date(JsonElement e) => DateOnly.Parse(e.GetString()!, CultureInfo.InvariantCulture);

    [Fact]
    public async Task Os_dois_cenarios_com_o_dataset_fixo_centavo_a_centavo()
    {
        var data = await WithDbAsync(CashFlowTests.SeedFin05Async);
        var clients = await WithDbAsync(SeedClientsAsync);
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var from = Today.AddDays(-7);
        var response = await owner.GetAsync($"{Base}?view=daily&days=90&from={Iso(from)}&scenario=both");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var flow = await JsonAsync(response);
        var adjusted = flow.GetProperty("adjusted");
        adjusted.GetProperty("tenantHasHistory").GetBoolean().Should().BeTrue();
        adjusted.GetProperty("tenantAvgDelayDays").GetDecimal().Should().Be(6.0m);
        adjusted.GetProperty("thresholdPct").GetDecimal().Should().Be(20m);

        // CA 1: cada dia, nos dois cenários, igual ao esperado.
        var (expectedBase, expectedAdjusted) = Expected();
        var basePeriods = flow.GetProperty("periods").EnumerateArray().ToList();
        var adjustedPeriods = adjusted.GetProperty("periods").EnumerateArray().ToList();
        basePeriods.Should().HaveCount(98);
        adjustedPeriods.Should().HaveCount(98);
        var lowestBase = Walk(basePeriods, expectedBase, "base");
        var lowestAdjusted = Walk(adjustedPeriods, expectedAdjusted, "ajustado");

        // CA 8: destaques e diferença.
        Date(flow.GetProperty("lowest").GetProperty("date")).Should().Be(lowestBase.Date);
        flow.GetProperty("lowest").GetProperty("balance").GetDecimal().Should().Be(lowestBase.Balance);
        Date(adjusted.GetProperty("lowest").GetProperty("date")).Should().Be(lowestAdjusted.Date);
        adjusted.GetProperty("lowest").GetProperty("balance").GetDecimal().Should().Be(lowestAdjusted.Balance);
        adjusted.GetProperty("lowestDifference").GetDecimal().Should().Be(lowestBase.Balance - lowestAdjusted.Balance);
        if (lowestAdjusted.FirstNegative is { } negative)
        {
            Date(adjusted.GetProperty("firstNegative").GetProperty("date")).Should().Be(negative);
        }
        else
        {
            adjusted.GetProperty("firstNegative").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // CA 3 e 5: risco de não receber (C a 40%) e o deslocado para depois do horizonte (A, 1.500 de +85 para +95).
        adjusted.GetProperty("atRiskTotal").GetDecimal().Should().Be(4_800m, "C: 1.600 + 2.000 + 1.200");
        var risk = adjusted.GetProperty("atRisk").EnumerateArray().Single();
        risk.GetProperty("name").GetString().Should().Be(clients.CName);
        risk.GetProperty("defaultPct").GetDecimal().Should().Be(40.00m);
        risk.GetProperty("count").GetInt32().Should().Be(3);
        adjusted.GetProperty("shiftedBeyond").GetProperty("count").GetInt32().Should().Be(1);
        adjusted.GetProperty("shiftedBeyond").GetProperty("total").GetDecimal().Should().Be(1_500m);
        (basePeriods.Sum(p => p.GetProperty("inflow").GetDecimal()) - adjustedPeriods.Sum(p => p.GetProperty("inflow").GetDecimal()))
            .Should().Be(4_800m + 1_500m, "base − ajustado = risco + deslocado");

        // CA 6: saídas iguais nos dois cenários, dia a dia.
        adjustedPeriods.Select(p => p.GetProperty("outflow").GetDecimal()).Should().Equal(basePeriods.Select(p => p.GetProperty("outflow").GetDecimal()));

        // CA 7: semanal e mensal batem com a diária nos dois cenários.
        foreach (var view in new[] { "weekly", "monthly" })
        {
            var grouped = await JsonAsync(await owner.GetAsync($"{Base}?view={view}&days=90&from={Iso(from)}"));
            var periods = grouped.GetProperty("adjusted").GetProperty("periods").EnumerateArray().ToList();
            periods.Sum(p => p.GetProperty("inflow").GetDecimal()).Should().Be(adjustedPeriods.Sum(p => p.GetProperty("inflow").GetDecimal()), view);
            periods.Sum(p => p.GetProperty("outflow").GetDecimal()).Should().Be(adjustedPeriods.Sum(p => p.GetProperty("outflow").GetDecimal()), view);
            periods[^1].GetProperty("balance").GetDecimal().Should().Be(adjustedPeriods[^1].GetProperty("balance").GetDecimal(), view);
        }

        await DayDetailAsync(owner, clients, adjustedPeriods);

        // CA 10: por conta, o ajustado segue a base: só no consolidado.
        var account = await JsonAsync(await owner.GetAsync($"{Base}?bankAccountId={data.BankA}&from={Iso(from)}"));
        account.GetProperty("consolidated").GetBoolean().Should().BeFalse();
        account.GetProperty("adjusted").ValueKind.Should().Be(JsonValueKind.Null);

        // Só a base: sem o cálculo do ajustado.
        (await JsonAsync(await owner.GetAsync($"{Base}?scenario=base"))).GetProperty("adjusted").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>Percorre a série: entradas, saídas e saldo de cada dia; devolve o menor saldo e o primeiro negativo do projetado.</summary>
    private static (DateOnly Date, decimal Balance, DateOnly? FirstNegative) Walk(List<JsonElement> periods, Dictionary<DateOnly, (decimal In, decimal Out)> expected, string scenario)
    {
        var balance = 12_100m;
        var lowest = (Date: DateOnly.MaxValue, Balance: decimal.MaxValue);
        DateOnly? negative = null;
        foreach (var p in periods)
        {
            var day = Date(p.GetProperty("start"));
            var (inflow, outflow) = expected.GetValueOrDefault(day);
            balance += inflow - outflow;
            p.GetProperty("inflow").GetDecimal().Should().Be(inflow, $"{scenario} {Iso(day)}");
            p.GetProperty("outflow").GetDecimal().Should().Be(outflow, $"{scenario} {Iso(day)}");
            p.GetProperty("balance").GetDecimal().Should().Be(balance, $"{scenario} {Iso(day)}");
            if (day >= Today && balance < lowest.Balance)
            {
                lowest = (day, balance);
            }

            if (day >= Today && balance < 0 && negative is null)
            {
                negative = day;
            }
        }

        return (lowest.Date, lowest.Balance, negative);
    }

    /// <summary>CA 9: o dia do ajustado com o vencimento original, a data ajustada e o motivo; o total igual ao da célula.</summary>
    private static async Task DayDetailAsync(HttpClient owner, Clients clients, List<JsonElement> adjustedPeriods)
    {
        decimal Cell(DateOnly d)
        {
            var p = adjustedPeriods.Single(x => Date(x.GetProperty("start")) == d);
            return p.GetProperty("inflow").GetDecimal() - p.GetProperty("outflow").GetDecimal();
        }

        var response = await owner.GetAsync($"{Base}/day?date={Iso(Today.AddDays(15))}&scenario=adjusted");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        response.Headers.CacheControl!.NoStore.Should().BeTrue("CA 17");
        var day = await JsonAsync(response);
        day.GetProperty("scenario").GetString().Should().Be("adjusted");
        var items = day.GetProperty("items").EnumerateArray().ToList();
        var a = items.Single(i => i.GetProperty("number").GetString() == "AJ-A3");
        a.GetProperty("dueDate").GetString().Should().Be(Iso(Today.AddDays(5)));
        a.GetProperty("adjustedDate").GetString().Should().Be(Iso(Today.AddDays(15)));
        a.GetProperty("reason").GetString().Should().Be($"Atraso médio de 10 dias de {clients.AName}.");
        var c = items.Single(i => i.GetProperty("number").GetString() == "AJ-C2");
        c.GetProperty("amount").GetDecimal().Should().Be(2_400m);
        c.GetProperty("originalAmount").GetDecimal().Should().Be(4_000m);
        c.GetProperty("reason").GetString().Should().EndWith("Valor ponderado pela inadimplência de 40,00%.");
        c.GetProperty("link").GetString().Should().StartWith("/app/financeiro/receber/");
        items.Where(i => i.GetProperty("reason").GetString()?.Contains(clients.AName, StringComparison.Ordinal) == true)
            .Should().OnlyContain(i => i.GetProperty("number").GetString()!.StartsWith("AJ-A", StringComparison.Ordinal), "só o atraso do cliente do título (CA 17)");
        day.GetProperty("total").GetDecimal().Should().Be(Cell(Today.AddDays(15)));

        // O dia de onde os títulos saíram: listados como deslocados, fora do total.
        var five = await JsonAsync(await owner.GetAsync($"{Base}/day?date={Iso(Today.AddDays(5))}&scenario=adjusted"));
        var shifted = five.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("shiftedOut").GetBoolean()).ToList();
        shifted.Select(i => i.GetProperty("number").GetString()).Should().Contain(["T-R1", "T-R5", "AJ-A3"]);
        shifted.Single(i => i.GetProperty("number").GetString() == "T-R1").GetProperty("reason").GetString().Should().Contain("Média da empresa: 6 dias.");
        five.GetProperty("total").GetDecimal().Should().Be(Cell(Today.AddDays(5)));

        var b = (await JsonAsync(await owner.GetAsync($"{Base}/day?date={Iso(Today.AddDays(26))}&scenario=adjusted"))).GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("number").GetString() == "AJ-B1");
        b.GetProperty("reason").GetString().Should().Be("Média da empresa: 6 dias.");

        var today = await JsonAsync(await owner.GetAsync($"{Base}/day?date={Iso(Today)}&scenario=adjusted"));
        today.GetProperty("total").GetDecimal().Should().Be(Cell(Today), "hoje: realizado, vencidos a pagar e os recebíveis que caem hoje");
    }
}
