using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class OpeningTests
{
    [Fact]
    public async Task Implantacao_do_corte_a_conclusao()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var financial = await ActorAsync("finance");
        var status = await JsonAsync(await owner.GetAsync(Base));
        status.GetProperty("cutoverDate").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        status.GetProperty("suggestedCutover").GetString().Should().Be(Iso(new DateOnly(Today.Year, Today.Month, 1).AddDays(-1)), "o último dia do mês anterior");

        // CA 1: só quem aprova; nunca no futuro.
        var cutover = Today.AddDays(-10);
        (await financial.PutAsJsonAsync(Base + "/cutover", new { cutoverDate = Iso(cutover) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PutAsJsonAsync(Base + "/cutover", new { cutoverDate = Iso(Today.AddDays(1)) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.SendAsync(Post(Base + "/receivables", new { lines = new[] { Receivable(Guid.NewGuid(), 1m) } }))).StatusCode.Should().Be(HttpStatusCode.Conflict, "sem data de corte não há lote");
        (await JsonAsync(await owner.PutAsJsonAsync(Base + "/cutover", new { cutoverDate = Iso(cutover) }))).GetProperty("cutoverDate").GetString().Should().Be(Iso(cutover));

        // CA 3: saldos no fim do dia de corte.
        var bank = await BankAsync(owner);
        var other = await BankAsync(owner);
        var balances = await owner.PutAsJsonAsync(Base + "/balances", new { lines = new object[] { new { bankAccountId = bank, balance = 1500.25m }, new { bankAccountId = other, balance = (decimal?)null } } });
        balances.StatusCode.Should().Be(HttpStatusCode.OK, await balances.Content.ReadAsStringAsync());
        var account = (await JsonAsync(balances)).GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == bank);
        account.GetProperty("openingBalance").GetDecimal().Should().Be(1500.25m);
        account.GetProperty("openingDate").GetString().Should().Be(Iso(cutover));
        account.GetProperty("systemBalanceAtCutover").GetDecimal().Should().Be(1500.25m);

        // CA 7: linha inválida aponta a linha e nada é gravado.
        var customer = await PartyAsync(owner, "Customer");
        var supplier = await PartyAsync(owner, "Supplier");
        var invalid = await owner.SendAsync(Post(Base + "/receivables", new { lines = new[] { Receivable(customer, 100m), Receivable(customer, 0m) } }));
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await invalid.Content.ReadAsStringAsync()).Should().Contain("lines[1].balance");
        var wrongRole = await owner.SendAsync(Post(Base + "/receivables", new { lines = new[] { Receivable(supplier, 100m) } }));
        (await wrongRole.Content.ReadAsStringAsync()).Should().Contain("lines[0].partyId");
        (await QueryAsync(db => db.Receivables.CountAsync(r => r.Origin == DocumentOrigin.Opening))).Should().Be(0);

        // CA 5: lote com uma chave; repetir a chave não duplica.
        var key = Guid.NewGuid();
        var lines = new { lines = new[] { Receivable(customer, 2500m, "FA2409-0012"), Receivable(customer, 300.50m) } };
        var batch = await owner.SendAsync(Post(Base + "/receivables", lines, key));
        batch.StatusCode.Should().Be(HttpStatusCode.OK, await batch.Content.ReadAsStringAsync());
        (await JsonAsync(batch)).GetProperty("total").GetDecimal().Should().Be(2800.50m);
        (await owner.SendAsync(Post(Base + "/receivables", lines, key))).StatusCode.Should().Be(HttpStatusCode.OK);
        var opened = await QueryAsync(db => db.Receivables.Where(r => r.Origin == DocumentOrigin.Opening).OrderBy(r => r.Number).ToListAsync());
        opened.Should().HaveCount(2);
        var fallback = await QueryAsync(db => db.Accounts.Where(a => a.Code == "1.9").Select(a => a.Id).SingleAsync());
        opened.Should().AllSatisfy(r => { r.AccountId.Should().Be(fallback); r.IssueDate.Should().Be(cutover); r.Number.Should().StartWith("CR-"); });
        opened.Select(r => r.OpeningReference).Should().Contain("FA2409-0012");
        (await QueryAsync(db => db.FinancialTransactions.CountAsync())).Should().Be(0, "título de implantação não cria transação");

        // CA 6: a pagar pelo saldo líquido, conta padrão 3.9.
        var payables = await owner.SendAsync(Post(Base + "/payables", new { lines = new[] { Payable(supplier, 800m), Payable(supplier, 1200m) } }));
        payables.StatusCode.Should().Be(HttpStatusCode.OK, await payables.Content.ReadAsStringAsync());
        var paid = await QueryAsync(db => db.Payables.Where(p => p.Origin == DocumentOrigin.Opening).ToListAsync());
        paid.Should().HaveCount(2).And.AllSatisfy(p => { p.RetentionIss.Should().Be(0); p.OpeningReference.Should().Be("FF-12"); });
        var expense = await QueryAsync(db => db.Accounts.Where(a => a.Code == "3.9").Select(a => a.Id).SingleAsync());
        paid.Select(p => p.AccountId).Should().AllBeEquivalentTo(expense);

        await AfterBatchesAsync(owner, financial, bank, cutover, opened, paid);
    }
}
