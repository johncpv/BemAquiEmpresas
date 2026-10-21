using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.Commercial;

public partial class QuoteTests
{
    [Fact]
    public async Task Aditivo_de_reducao_revalida_e_reduz_o_titulo_futuro_e_reprovar_exige_motivo()
    {
        var (_, author) = await ActorAsync(Group.OwnerSeedKey);
        var (_, approver) = await ActorAsync(Group.OwnerSeedKey);
        var (id, quote, _) = await SentQuoteAsync(author, canary: false);
        var (contractId, _) = await ApproveAsync(author, id, quote, Today);
        var entrega = (await ParcelsAsync(contractId)).Single(p => p.Status == ReceivableStatus.Planned);
        var path = $"/api/v1/contracts/{contractId}/amendments";
        object Body(decimal delta, decimal newAmount) => new
        {
            description = "Tirou um item", valueDelta = delta, daysDelta = 0, milestones = (object[]?)null, reductions = new[] { new { receivableId = entrega.Id, newAmount } },
            budgetDeltas = (object?)null, version = (uint?)null,
        };

        // CA 3 e 4: a redução não passa do saldo sem baixa e precisa fechar com a variação.
        var tooMuch = await author.SendAsync(Post(path, Body(-10_000_000m, 0m)));
        tooMuch.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await tooMuch.Content.ReadAsStringAsync()).Should().Contain("A redução é maior que o saldo a receber sem baixa.");
        (await author.SendAsync(Post(path, Body(-100m, entrega.Amount - 50m)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var amendment = await JsonAsync(await author.SendAsync(Post(path, Body(-100m, entrega.Amount - 100m))));
        var amendmentId = amendment.GetProperty("id").GetGuid();
        amendment = await JsonAsync(await author.PostAsJsonAsync($"{path}/{amendmentId}/submit", new { version = Version(amendment) }));
        (await approver.PostAsJsonAsync($"{path}/{amendmentId}/approve", new { version = Version(amendment) })).StatusCode.Should().Be(HttpStatusCode.OK);
        await DrainAsync();
        (await ParcelsAsync(contractId)).Single(p => p.Id == entrega.Id).Amount.Should().Be(entrega.Amount - 100m);

        // CA 8: reprovar exige comentário.
        var second = await JsonAsync(await author.SendAsync(Post(path, new { description = "Prazo", valueDelta = 0m, daysDelta = 5, milestones = (object[]?)null, reductions = (object[]?)null, budgetDeltas = (object?)null, version = (uint?)null })));
        second.GetProperty("number").GetInt32().Should().Be(2);
        var secondId = second.GetProperty("id").GetGuid();
        second = await JsonAsync(await author.PostAsJsonAsync($"{path}/{secondId}/submit", new { version = Version(second) }));
        (await approver.PostAsJsonAsync($"{path}/{secondId}/reject", new { comment = " ", version = Version(second) })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var rejected = await JsonAsync(await approver.PostAsJsonAsync($"{path}/{secondId}/reject", new { comment = "Sem justificativa", version = Version(second) }));
        rejected.GetProperty("status").GetString().Should().Be("Rejected");
    }
}
