using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class ReceivablesTests
{
    [Fact]
    public async Task Invariante_saldo_igual_a_soma_das_baixas_ativas_em_operacoes_aleatorias()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var bank = await BankAsync(owner);
        var random = new Random(20261020);
        var current = await ReceivableAsync(owner, party, 1000m);
        var id = current.GetProperty("id").GetGuid();
        for (var step = 0; step < 15; step++)
        {
            var active = (await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{id}/settlements"))).EnumerateArray()
                .Where(s => s.GetProperty("reversedAt").ValueKind == JsonValueKind.Null).Select(s => s.GetProperty("id").GetGuid()).ToList();
            var balance = current.GetProperty("balance").GetDecimal();
            HttpResponseMessage response;
            if (active.Count > 0 && (balance == 0 || random.Next(3) == 0))
            {
                response = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements/{active[random.Next(active.Count)]}/reverse", new { reason = "Teste", version = Version(current) });
            }
            else if (random.Next(4) == 0)
            {
                response = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/write-off", new { date = Iso(Today), backdateReason = (string?)null, version = Version(current) });
            }
            else
            {
                var principal = Math.Max(0.01m, Math.Round(balance * (decimal)random.NextDouble(), 2));
                response = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, principal, Version(current), interest: random.Next(3)));
            }

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            current = await JsonAsync(response);
            var (paid, sum, status) = await QueryAsync(async db =>
            {
                var r = await db.Receivables.AsNoTracking().SingleAsync(x => x.Id == id);
                var principal = await db.ReceivableSettlements.Where(s => s.ReceivableId == id && s.ReversedAt == null).SumAsync(s => s.Principal);
                return (r.PaidAmount, principal, r.Status);
            });
            paid.Should().Be(sum, "CA 12, passo " + step);
            status.Should().Be(paid == 0 ? ReceivableStatus.Open : paid >= 1000m ? ReceivableStatus.Paid : ReceivableStatus.PartiallyPaid);
        }
    }
}
