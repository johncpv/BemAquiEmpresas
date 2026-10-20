using System.Net;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

public partial class BankingTests
{
    [Fact]
    public async Task Extrato_com_saldo_inicial_saldo_anterior_e_acumulado()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var d0 = Today.AddDays(-20);
        var inter = await BankAsync(owner, 10_000m, d0, "Inter");
        await WithDbAsync(db =>
        {
            db.FinancialTransactions.AddRange(
                new FinancialTransaction { BankAccountId = inter, Date = d0.AddDays(4), Amount = 425m, Type = TransactionType.Receipt, Description = "Recebimento", CreatedAt = DateTimeOffset.UtcNow },
                new FinancialTransaction { BankAccountId = inter, Date = d0.AddDays(5), Amount = -1_200m, Type = TransactionType.Payment, Description = "Pagamento", CreatedAt = DateTimeOffset.UtcNow });
            return db.SaveChangesAsync();
        });

        var statement = await JsonAsync(await owner.GetAsync($"{Base}/bank-accounts/{inter}/statement?from={Iso(d0)}&to={Iso(d0.AddDays(29))}"));
        statement.GetProperty("startLabel").GetString().Should().Be("Saldo inicial");
        statement.GetProperty("startBalance").GetDecimal().Should().Be(10_000m);
        statement.GetProperty("lines").GetProperty("items").EnumerateArray().Select(l => l.GetProperty("balance").GetDecimal()).Should().Equal(10_425m, 9_225m);
        statement.GetProperty("endBalance").GetDecimal().Should().Be(9_225m);

        // CA 2: o saldo anterior bate com o fim do período anterior.
        var previous = await JsonAsync(await owner.GetAsync($"{Base}/bank-accounts/{inter}/statement?from={Iso(d0)}&to={Iso(d0.AddDays(4))}"));
        var later = await JsonAsync(await owner.GetAsync($"{Base}/bank-accounts/{inter}/statement?from={Iso(d0.AddDays(5))}&to={Iso(d0.AddDays(29))}"));
        later.GetProperty("startLabel").GetString().Should().Be("Saldo anterior");
        later.GetProperty("startBalance").GetDecimal().Should().Be(previous.GetProperty("endBalance").GetDecimal()).And.Be(10_425m);
        (await owner.GetAsync($"{Base}/bank-accounts/{Guid.NewGuid()}/statement")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var balances = await JsonAsync(await owner.GetAsync($"{Base}/bank-accounts/balances"));
        balances.GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == inter).GetProperty("currentBalance").GetDecimal().Should().Be(9_225m);
    }
}
