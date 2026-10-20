using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class ReceivablesTests
{
    [Fact]
    public async Task Estorno_cria_transacao_inversa_e_o_cancelamento_exige_estornar_antes()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var bank = await BankAsync(owner);
        var receivable = await ReceivableAsync(owner, party, 500m);
        var id = receivable.GetProperty("id").GetGuid();
        var settled = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements", Settle(bank, 200m, Version(receivable), interest: 10m)));
        var cancelDenied = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/cancel", new { reason = "Venda desfeita", version = Version(settled) });
        cancelDenied.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await cancelDenied.Content.ReadAsStringAsync()).Should().Contain("Estorne as baixas antes de cancelar.");

        var settlement = (await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{id}/settlements")))[0].GetProperty("id").GetGuid();
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements/{settlement}/reverse", new { reason = " ", version = Version(settled) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "motivo obrigatório");
        var reversed = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements/{settlement}/reverse", new { reason = "Cheque devolvido", version = Version(settled) }));
        reversed.GetProperty("status").GetString().Should().Be("Open");
        reversed.GetProperty("paidAmount").GetDecimal().Should().Be(0m);
        (await QueryAsync(db => db.FinancialTransactions.Where(t => t.ReceivableId == id).OrderBy(t => t.CreatedAt).Select(t => t.Amount).ToListAsync())).Should().Equal(210m, -210m);
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/settlements/{settlement}/reverse", new { reason = "De novo", version = Version(reversed) })).StatusCode.Should().Be(HttpStatusCode.Conflict, "já estornada");
        var number = receivable.GetProperty("number").GetString()!;
        (await QueryAsync(db => db.Notifications.CountAsync(n => n.Title.StartsWith("Baixa de " + number + " estornada")))).Should().BeGreaterThan(0);

        var cancelled = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/cancel", new { reason = "Venda desfeita", version = Version(reversed) }));
        cancelled.GetProperty("status").GetString().Should().Be("Cancelled");
        (await QueryAsync(db => db.Notifications.CountAsync(n => n.Title.StartsWith("Título " + number + " de")))).Should().BeGreaterThan(0, "todo cancelamento avisa o Proprietário (CA 17)");
        (await owner.DeleteAsync($"/api/v1/finance/receivables/{id}")).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed, "título nunca é apagado (CA 21)");
    }

    [Fact]
    public async Task Cancelar_acima_do_limite_so_com_aprovacao()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, financial) = await ActorAsync("finance");
        var party = await CustomerAsync(owner);
        var big = await ReceivableAsync(owner, party, 1500m);
        var small = await ReceivableAsync(owner, party, 900m);
        var denied = await financial.PostAsJsonAsync($"/api/v1/finance/receivables/{big.GetProperty("id").GetGuid()}/cancel", new { reason = "Erro", version = Version(big) });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(denied)).Should().Be("cancel_requires_approval");
        (await financial.PostAsJsonAsync($"/api/v1/finance/receivables/{small.GetProperty("id").GetGuid()}/cancel", new { reason = "Erro", version = Version(small) })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{big.GetProperty("id").GetGuid()}/cancel", new { reason = "Erro", version = Version(big) })).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await owner.GetAsync("/api/v1/finance/receivables/discounts-and-cancellations");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var report = await JsonAsync(response);
        report.GetProperty("cancellations").EnumerateArray().Select(c => c.GetProperty("receivableId").GetGuid()).Should().Contain([big.GetProperty("id").GetGuid(), small.GetProperty("id").GetGuid()]);
    }
}
