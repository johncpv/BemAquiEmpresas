using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Finance;

public partial class BankingTests
{
    [Fact]
    public async Task Transferencia_conta_nova_aviso_ao_Proprietario_e_estorno()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, finance) = await ActorAsync("finance");
        var from = await BankAsync(owner, 50_000m, Today.AddDays(-40), "Origem");
        var to = await BankAsync(owner, 0m, Today.AddDays(-40), "Destino");
        await AgeAsync(from);
        object Body(decimal amount, DateOnly? date = null, Guid? target = null) => new { fromAccountId = from, toAccountId = target ?? to, amount, date = Iso(date ?? Today), description = (string?)null, backdateReason = (string?)null };

        // CA 8: destino criado há menos de 30 dias, só quem aprova.
        var denied = await finance.SendAsync(Post($"{Base}/transfers", Body(100m)));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(denied)).Should().Be("transfer_to_new_account");
        await AgeAsync(to);

        // CA 6: mesma conta, valor zero, data antes do saldo inicial.
        (await finance.SendAsync(Post($"{Base}/transfers", Body(10m, target: from)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await finance.SendAsync(Post($"{Base}/transfers", Body(0m)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await (await finance.SendAsync(Post($"{Base}/transfers", Body(10m, Today.AddDays(-50))))).Content.ReadAsStringAsync()).Should().Contain("anterior ao saldo inicial");

        // CA 5 e 7: duas transações ligadas; acima de R$ 10.000,00 avisa o Proprietário; mesma chave não duplica.
        var key = Guid.NewGuid();
        var created = await finance.SendAsync(Post($"{Base}/transfers", Body(15_000m), key));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await finance.SendAsync(Post($"{Base}/transfers", Body(15_000m), key))).StatusCode.Should().Be(HttpStatusCode.Created);
        var transfer = await JsonAsync(created);
        var id = transfer.GetProperty("id").GetGuid();
        (await WithDbAsync(db => db.FinancialTransactions.Where(t => t.TransferId == id).Select(t => t.Amount).ToListAsync())).Should().BeEquivalentTo([-15_000m, 15_000m]);
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.Title.StartsWith("Transferência de R$") && n.Title.Contains("15.000,00")))).Should().BeGreaterThan(0);

        // CA 10: estorno com as inversas.
        var version = transfer.GetProperty("version").GetUInt32();
        var reversed = await finance.PostAsJsonAsync($"{Base}/transfers/{id}/reverse", new { reason = "Conta errada", version });
        reversed.StatusCode.Should().Be(HttpStatusCode.OK, await reversed.Content.ReadAsStringAsync());
        (await WithDbAsync(db => db.FinancialTransactions.Where(t => t.TransferId == id).SumAsync(t => t.Amount))).Should().Be(0m);
        (await finance.PostAsJsonAsync($"{Base}/transfers/{id}/reverse", new { reason = "De novo", version })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
