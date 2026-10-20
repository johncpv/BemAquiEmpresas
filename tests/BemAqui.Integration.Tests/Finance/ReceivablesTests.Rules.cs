using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class ReceivablesTests
{
    [Fact]
    public async Task Data_retroativa_pede_motivo_e_avisa_o_proprietario_e_nunca_antes_do_saldo_inicial()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var bank = await BankAsync(owner, Iso(Today.AddDays(-20)));
        var receivable = await ReceivableAsync(owner, party);
        var id = receivable.GetProperty("id").GetGuid();
        var missing = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 100m, Version(receivable), date: Today.AddDays(-10)));
        missing.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("Informe o motivo da data retroativa.");
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 100m, Version(receivable), date: Today.AddDays(-25), reason: "Extrato atrasado")))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "antes do saldo inicial da conta (CA 24)");
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 100m, Version(receivable), date: Today.AddDays(-10), reason: "Extrato atrasado")))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var number = receivable.GetProperty("number").GetString()!;
        (await QueryAsync(db => db.Notifications.CountAsync(n => n.Title.StartsWith("Baixa de " + number + " com data")))).Should().BeGreaterThan(0, "Proprietário avisado (CA 14)");
        var locked = await owner.PutAsJsonAsync($"/api/v1/finance/bank-accounts/{bank}", new { name = "Outro nome", kind = "Bank", bankCode = "077", agency = "1234-5", account = "12345678-9", openingBalance = 5m, openingDate = Iso(Today.AddDays(-20)), isDefault = false });
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict, "com transação, o saldo inicial trava (CA 23)");
    }

    [Fact]
    public async Task Duas_baixas_ao_mesmo_tempo_uma_recebe_409()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var bank = await BankAsync(owner);
        var receivable = await ReceivableAsync(owner, party);
        var id = receivable.GetProperty("id").GetGuid();
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 700m, Version(receivable)))));
        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        var conflict = results.Single(r => r.StatusCode != HttpStatusCode.OK);
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await conflict.Content.ReadAsStringAsync()).Should().Contain("concurrency_conflict").And.Contain("O título mudou desde que você abriu. Recarregue.");
        (await QueryAsync(db => db.ReceivableSettlements.CountAsync(s => s.ReceivableId == id))).Should().Be(1, "CA 15");
    }

    [Fact]
    public async Task Situacao_vencido_e_vence_hoje_sao_calculadas()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var late = await ReceivableAsync(owner, party, 10m, Today.AddDays(-1));
        var today = await ReceivableAsync(owner, party, 10m, Today);
        late.GetProperty("isOverdue").GetBoolean().Should().BeTrue();
        today.GetProperty("isOverdue").GetBoolean().Should().BeFalse();
        today.GetProperty("isDueToday").GetBoolean().Should().BeTrue();
        (await QueryAsync(db => db.Receivables.Where(r => r.Id == late.GetProperty("id").GetGuid()).Select(r => r.Status).SingleAsync())).Should().Be(ReceivableStatus.Open, "nada de Overdue gravado");
        var overdue = await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables?situation=vencido&partyId={party}"));
        overdue.GetProperty("page").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).Should().Equal(late.GetProperty("id").GetGuid());
        overdue.GetProperty("totals").GetProperty("overdue").GetDecimal().Should().Be(10m);
    }
}
