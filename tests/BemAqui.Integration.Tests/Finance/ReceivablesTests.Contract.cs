using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Finance;

public partial class ReceivablesTests
{
    [Fact]
    public async Task Titulo_do_contrato_muda_so_o_vencimento_e_com_origem_cancelada_pode_cancelar()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var party = await CustomerAsync(owner);
        var account = await RevenueAsync();
        var contract = Guid.CreateVersion7();
        var id = await QueryAsync(async db =>
        {
            var r = new Receivable { Number = "CR-T-" + Guid.NewGuid().ToString("N")[..8], PartyId = party, Description = "Sinal", AccountId = account, Amount = 3000m, IssueDate = Today, DueDate = Today.AddDays(5), Origin = DocumentOrigin.Contract, OriginId = contract };
            db.Receivables.Add(r);
            await db.SaveChangesAsync();
            return r.Id;
        });
        var current = await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{id}"));
        var amount = await owner.PutAsJsonAsync($"/api/v1/finance/receivables/{id}", new { description = "Sinal", accountId = account, costCenterId = (Guid?)null, projectId = (Guid?)null, amount = 1m, version = Version(current) });
        amount.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await amount.Content.ReadAsStringAsync()).Should().Contain("ajuste pelo contrato ou pelo aditivo");
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/due-date", new { dueDate = Iso(Today.AddDays(20)), reason = "", version = Version(current) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var moved = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/due-date", new { dueDate = Iso(Today.AddDays(20)), reason = "Cliente pediu", version = Version(current) }));
        moved.GetProperty("dueDate").GetString().Should().Be(Iso(Today.AddDays(20)), "D14");
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/cancel", new { reason = "Erro", version = Version(moved) })).StatusCode.Should().Be(HttpStatusCode.Conflict, "contrato ativo");

        CancelledContracts[contract] = true;
        var cancelled = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{id}/cancel", new { reason = "Contrato cancelado", version = Version(moved) }));
        cancelled.GetProperty("cancelReason").GetString().Should().StartWith("Origem cancelada", "CA 20");
    }

    [Fact]
    public async Task Tres_camadas_e_baixa_de_outro_titulo_e_404()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("view");
        var (_, nobody) = await ActorAsync();
        var party = await CustomerAsync(owner);
        var bank = await BankAsync(owner);
        var a = await ReceivableAsync(owner, party);
        var b = await ReceivableAsync(owner, party);
        (await nobody.GetAsync("/api/v1/finance/receivables")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.GetAsync("/api/v1/finance/receivables")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.SendAsync(Post("/api/v1/finance/receivables", new { partyId = party, description = "X", accountId = await RevenueAsync(), costCenterId = (Guid?)null, projectId = (Guid?)null, amount = 1m, issueDate = "2026-10-01", dueDate = "2026-10-02" }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync($"/api/v1/finance/receivables/{a.GetProperty("id").GetGuid()}/settlements", Settle(bank, 1m, Version(a)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{a.GetProperty("id").GetGuid()}/settlements", Settle(bank, 1m, Version(a)));
        var settlement = (await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{a.GetProperty("id").GetGuid()}/settlements")))[0].GetProperty("id").GetGuid();
        (await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{b.GetProperty("id").GetGuid()}/settlements/{settlement}/reverse", new { reason = "x", version = Version(b) })).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR pai/filho (CA 26)");
        (await owner.GetStringAsync($"/api/v1/audit/Receivable/{a.GetProperty("id").GetGuid()}")).Should().Contain("Created", "histórico (CA 27)");
    }
}
