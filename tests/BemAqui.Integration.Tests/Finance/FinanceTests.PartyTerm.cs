using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

public partial class FinanceTests
{
    [Fact]
    public async Task Condicao_padrao_do_terceiro_so_ativa_e_em_uso_nao_e_excluida()
    {
        var (_, owner) = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var term = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/payment-terms", Term("45 dias " + Guid.NewGuid().ToString("N")[..4], (45, 100m))));
        (await JsonAsync(await owner.GetAsync("/api/v1/parties/payment-terms"))).EnumerateArray().Should().Contain(o => o.GetProperty("id").GetGuid() == term);

        var party = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Cliente com condição", roles = new[] { "Customer" }, defaultPaymentTermId = term }));
        (await JsonAsync(await owner.GetAsync($"/api/v1/parties/{party}"))).GetProperty("defaultPaymentTerm").GetProperty("id").GetGuid().Should().Be(term);

        var invalid = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", legalName = "Condição falsa", roles = new[] { "Customer" }, defaultPaymentTermId = Guid.CreateVersion7() });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await invalid.Content.ReadAsStringAsync()).Should().Contain("Condição de pagamento inválida.");

        var delete = await owner.DeleteAsync($"/api/v1/finance/payment-terms/{term}");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await delete.Content.ReadAsStringAsync()).Should().Contain("A condição está em uso. Inative-a.");
        (await owner.PostAsync($"/api/v1/finance/payment-terms/{term}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await JsonAsync(await owner.GetAsync("/api/v1/parties/payment-terms"))).EnumerateArray().Should().NotContain(o => o.GetProperty("id").GetGuid() == term, "inativa some dos seletores");
        (await JsonAsync(await owner.GetAsync($"/api/v1/parties/{party}"))).GetProperty("defaultPaymentTerm").GetProperty("isActive").GetBoolean().Should().BeFalse("e continua no terceiro que a usa (CA 17)");
        await using var scope = await ScopeAsync();
        (await scope.ServiceProvider.GetRequiredService<BemAqui.Parties.Contracts.IParties>().FindAsync(party))!.DefaultPaymentTermId.Should().Be(term);
    }
}
