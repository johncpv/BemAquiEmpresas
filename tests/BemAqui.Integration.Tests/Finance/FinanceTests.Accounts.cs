using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class FinanceTests
{
    [Fact]
    public async Task Banco_e_caixa_com_saldo_opcional_e_so_valendo_junto_com_a_data()
    {
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);

        var bank = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Inter PJ", balance: -150.25m, date: "2026-10-01")));
        bank.GetProperty("bankName").GetString().Should().Be("Banco Inter");
        bank.GetProperty("openingBalance").GetDecimal().Should().Be(-150.25m, "saldo negativo é aceito, com aviso só na tela (CA 3)");
        bank.GetProperty("openingDate").GetString().Should().Be("2026-10-01");

        var cash = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Caixa da fábrica", kind = "Cash", bankCode = "999", agency = "x", account = "y" }));
        cash.GetProperty("bankCode").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "no caixa, banco, agência e conta não existem (CA 2)");
        cash.GetProperty("openingBalance").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        var noDate = await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Sem data", balance: 10m));
        noDate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await noDate.Content.ReadAsStringAsync()).Should().Contain("Informe a data do saldo inicial.");
        (await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Agência errada", agency: "12a"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Banco inexistente", kind = "Bank", bankCode = "999", agency = "1", account = "1-1" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task So_uma_conta_padrao_e_a_padrao_nao_e_inativada_nem_excluida()
    {
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var first = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Conta A")));
        (await QueryAsync(db => db.BankAccounts.CountAsync(a => a.IsDefault))).Should().Be(1, "a primeira conta nasce padrão e só existe uma (CA 6)");

        var second = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Conta B", isDefault: true)));
        (await QueryAsync(db => db.BankAccounts.Where(a => a.IsDefault).Select(a => a.Id).ToListAsync())).Should().Equal(second);
        (await owner.PutAsJsonAsync($"/api/v1/finance/bank-accounts/{second}", Bank("Conta B editada"))).StatusCode.Should().Be(HttpStatusCode.OK, "editar sem marcar padrão não desmarca a padrão");
        (await QueryAsync(db => db.BankAccounts.Where(a => a.IsDefault).Select(a => a.Id).ToListAsync())).Should().Equal(second);

        var inactivate = await owner.PostAsync($"/api/v1/finance/bank-accounts/{second}/inactivate", null);
        inactivate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await inactivate.Content.ReadAsStringAsync()).Should().Contain("Escolha outra conta padrão antes.");
        (await owner.DeleteAsync($"/api/v1/finance/bank-accounts/{second}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.PostAsync($"/api/v1/finance/bank-accounts/{first}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Corrida " + i, isDefault: true))));
        results.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created || r.StatusCode == HttpStatusCode.Conflict);
        (await QueryAsync(db => db.BankAccounts.CountAsync(a => a.IsDefault))).Should().Be(1, "gravações simultâneas: o índice único parcial deixa uma só");
    }

    [Fact]
    public async Task Com_movimentacao_o_saldo_trava_e_a_conta_nao_e_excluida()
    {
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        // Outra conta fica padrão: a padrão não é excluída (CA 7), e aqui o assunto é a movimentação.
        await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Padrão do teste", isDefault: true)));
        var free = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Livre")));
        var used = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", Bank("Usada", balance: 100m, date: "2026-10-01")));
        Moved[used] = true;

        var balance = await owner.PutAsJsonAsync($"/api/v1/finance/bank-accounts/{used}", Bank("Usada", balance: 200m, date: "2026-10-01"));
        balance.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await balance.Content.ReadAsStringAsync()).Should().Contain("A conta já tem movimentação. Ajuste o saldo por um lançamento.");
        (await owner.PutAsJsonAsync($"/api/v1/finance/bank-accounts/{used}", Bank("Usada renomeada", balance: 100m, date: "2026-10-01"))).StatusCode.Should().Be(HttpStatusCode.OK, "o nome continua editável");

        var delete = await owner.DeleteAsync($"/api/v1/finance/bank-accounts/{used}");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(delete)).Should().Be("bank_account_in_use");
        (await owner.DeleteAsync($"/api/v1/finance/bank-accounts/{free}")).IsSuccessStatusCode.Should().BeTrue("sem movimentação, exclusão lógica (CA 8)");
        (await QueryAsync(db => db.BankAccounts.IgnoreQueryFilters().SingleAsync(a => a.Id == free))).IsDeleted.Should().BeTrue();
    }
}
