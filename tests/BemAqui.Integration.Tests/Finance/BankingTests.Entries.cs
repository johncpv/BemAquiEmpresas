using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class BankingTests
{
    [Fact]
    public async Task Lancamento_sem_OP_com_conta_sugerida_aviso_filtro_e_estorno()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, finance) = await ActorAsync("finance");
        var bank = await BankAsync(owner, 1_000m, Today.AddDays(-40), "Tarifas");
        object Entry(string? reason) => new { bankAccountId = bank, kind = "BankFee", date = Iso(Today), amount = 12.5m, accountId = (Guid?)null, costCenterId = (Guid?)null, reason, backdateReason = (string?)null };
        (await finance.SendAsync(Post($"{Base}/bank-entries", Entry(" ")))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "motivo obrigatório");
        var created = await finance.SendAsync(Post($"{Base}/bank-entries", Entry("Tarifa de manutenção")));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var entry = await JsonAsync(created);
        entry.GetProperty("amount").GetDecimal().Should().Be(-12.5m);
        var expense = await WithDbAsync(db => db.Accounts.Where(a => a.Code == "3.5").Select(a => a.Id).SingleAsync());
        entry.GetProperty("accountId").GetGuid().Should().Be(expense, "tarifa sugere 3.5 Financeiras");
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.Title.StartsWith("Tarifa bancária de R$") && n.Title.Contains("sem OP")))).Should().BeGreaterThan(0);

        var onlyEntries = await JsonAsync(await owner.GetAsync($"{Base}/bank-accounts/{bank}/statement?withoutOrder=true"));
        onlyEntries.GetProperty("lines").GetProperty("items").EnumerateArray().Should().ContainSingle().Which.GetProperty("withoutOrder").GetBoolean().Should().BeTrue();
        var id = entry.GetProperty("id").GetGuid();
        (await finance.PostAsJsonAsync($"{Base}/bank-entries/{id}/reverse", new { reason = "Tarifa devolvida", version = entry.GetProperty("version").GetUInt32() })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await WithDbAsync(db => db.FinancialTransactions.Where(t => t.BankEntryId == id).SumAsync(t => t.Amount))).Should().Be(0m);
        var (_, viewer) = await ActorAsync(null);
        (await viewer.SendAsync(Post($"{Base}/bank-entries", Entry("x")))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
