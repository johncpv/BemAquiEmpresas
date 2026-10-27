using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class OpeningTests
{
    private async Task AfterBatchesAsync(HttpClient owner, HttpClient financial, Guid bank, DateOnly cutover, List<Receivable> opened, List<Payable> paid)
    {
        // CA 8: acima do limite, cancela sem aprovar enquanto a implantação está aberta.
        var big = opened.Single(r => r.Amount == 2500m);
        var detail = await JsonAsync(await financial.GetAsync($"/api/v1/finance/receivables/{big.Id}"));
        var cancelled = await financial.PostAsJsonAsync($"/api/v1/finance/receivables/{big.Id}/cancel", new { reason = "Saldo errado", version = detail.GetProperty("version").GetUInt32() });
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK, await cancelled.Content.ReadAsStringAsync());

        // CA 9: conferência por cliente e fornecedor, sem o cancelado.
        var status = await JsonAsync(await financial.GetAsync(Base));
        status.GetProperty("receivables").EnumerateArray().Single().GetProperty("total").GetDecimal().Should().Be(300.50m);
        status.GetProperty("payables").EnumerateArray().Single().GetProperty("count").GetInt32().Should().Be(2);

        // CA 2 e 4: depois do primeiro movimento, nem a data nem o saldo mudam.
        var small = opened.Single(r => r.Amount == 300.50m);
        var version = (await JsonAsync(await owner.GetAsync($"/api/v1/finance/receivables/{small.Id}"))).GetProperty("version").GetUInt32();
        var settled = await owner.PostAsJsonAsync($"/api/v1/finance/receivables/{small.Id}/settlements", new { bankAccountId = bank, date = Iso(Today), principal = 300.50m, interest = 0m, fine = 0m, discount = 0m, backdateReason = (string?)null, version });
        settled.StatusCode.Should().Be(HttpStatusCode.OK, await settled.Content.ReadAsStringAsync());
        var locked = await owner.PutAsJsonAsync(Base + "/cutover", new { cutoverDate = Iso(cutover.AddDays(-1)) });
        locked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(locked)).Should().Be("cutover_locked");
        var moved = await owner.PutAsJsonAsync(Base + "/balances", new { lines = new object[] { new { bankAccountId = bank, balance = 10m } } });
        Code(await JsonAsync(moved)).Should().Be("bank_account_has_movement");

        // CA 10: só quem aprova conclui; depois disso, nada de lote novo.
        status = await JsonAsync(await owner.GetAsync(Base));
        var complete = new { notes = "Diferença de tarifa do mês", version = status.GetProperty("version").GetUInt32() };
        (await financial.PostAsJsonAsync(Base + "/complete", complete)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var done = await owner.PostAsJsonAsync(Base + "/complete", complete);
        done.StatusCode.Should().Be(HttpStatusCode.OK, await done.Content.ReadAsStringAsync());
        (await JsonAsync(done)).GetProperty("completedAt").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
        var refused = await owner.SendAsync(Post(Base + "/payables", new { lines = new[] { Payable(paid[0].PartyId!.Value, 10m) } }));
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("A implantação foi concluída em");

        // Concluída, o cancelamento volta a pedir o limite.
        var bigPayable = paid.Single(p => p.Amount == 1200m);
        var pv = (await JsonAsync(await financial.GetAsync($"/api/v1/finance/payables/{bigPayable.Id}"))).GetProperty("version").GetUInt32();
        (await financial.PostAsJsonAsync($"/api/v1/finance/payables/{bigPayable.Id}/cancel", new { reason = "Erro", version = pv })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // CA 15: histórico da data de corte, dos saldos, dos lotes e da conclusão.
        var audited = await QueryAsync(db => db.AuditEntries.Select(a => a.Entity).Distinct().ToListAsync());
        audited.Should().Contain([nameof(OpeningSetup), nameof(OpeningBatch), nameof(BankAccount)]);
    }
}
