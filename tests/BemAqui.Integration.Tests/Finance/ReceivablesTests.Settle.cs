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
    public async Task Baixa_parcial_e_total_gravam_a_transacao_e_o_saldo()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var bank = await BankAsync(owner);
        var receivable = await ReceivableAsync(owner, party);
        var id = receivable.GetProperty("id").GetGuid();

        var partial = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 400m, Version(receivable), interest: 5m, fine: 20m));
        partial.StatusCode.Should().Be(HttpStatusCode.OK, await partial.Content.ReadAsStringAsync());
        var afterPartial = await JsonAsync(partial);
        afterPartial.GetProperty("status").GetString().Should().Be("PartiallyPaid");
        afterPartial.GetProperty("balance").GetDecimal().Should().Be(600m);
        (await QueryAsync(db => db.FinancialTransactions.Where(t => t.ReceivableId == id).Select(t => t.Amount).ToListAsync())).Should().Equal(425m);

        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 700m, Version(afterPartial)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "principal passa do saldo");
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 100m, Version(afterPartial), discount: 200m))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "desconto passa do principal");
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 100m, Version(afterPartial), date: Today.AddDays(1)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "data futura");

        var full = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 600m, Version(afterPartial))));
        full.GetProperty("status").GetString().Should().Be("Paid");
        full.GetProperty("paidAmount").GetDecimal().Should().Be(1000m);
        full.GetProperty("interest").GetDecimal().Should().Be(5m);
        full.GetProperty("fine").GetDecimal().Should().Be(20m);
    }

    [Fact]
    public async Task Quitacao_com_desconto_nao_movimenta_conta_e_respeita_o_limite()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, financial) = await ActorAsync("finance");
        var party = await CustomerAsync(owner);
        var bank = await BankAsync(owner);
        var receivable = await ReceivableAsync(owner, party, 600m);
        var id = receivable.GetProperty("id").GetGuid();
        var settled = await JsonAsync(await financial.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 599.50m, Version(receivable))));

        var denied = await financial.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/write-off", new { date = Iso(Today), backdateReason = (string?)null, version = Version(settled) });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "quitar 100% do saldo passa do limite de 5% (CA 13)");
        Code(await JsonAsync(denied)).Should().Be("discount_requires_approval");

        var quit = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/write-off", new { date = Iso(Today), backdateReason = (string?)null, version = Version(settled) }));
        quit.GetProperty("status").GetString().Should().Be("Paid");
        quit.GetProperty("discount").GetDecimal().Should().Be(0.50m);
        (await QueryAsync(db => db.FinancialTransactions.CountAsync(t => t.ReceivableId == id))).Should().Be(1, "a quitação não cria transação (CA 11)");
    }

    [Fact]
    public async Task Desconto_acima_do_limite_so_com_aprovacao()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, financial) = await ActorAsync("finance");
        var party = await CustomerAsync(owner);
        var bank = await BankAsync(owner);
        var receivable = await ReceivableAsync(owner, party, 600m);
        var id = receivable.GetProperty("id").GetGuid();
        var within = await financial.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 100m, Version(receivable), discount: 30m));
        within.StatusCode.Should().Be(HttpStatusCode.OK, "30 de 600 = 5%, no limite");
        var current = await JsonAsync(within);
        var over = await financial.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 100m, Version(current), discount: 40m));
        over.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(over)).Should().Be("discount_requires_approval");
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 100m, Version(current), discount: 40m))).StatusCode.Should().Be(HttpStatusCode.OK, "o Proprietário aprova");
    }
}
