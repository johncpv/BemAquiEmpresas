using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

public partial class ChartTests
{
    [Fact]
    public async Task Conta_com_lancamento_nao_vira_agrupadora_nem_e_excluida_e_a_sintetica_so_inativa_sem_filhas_ativas()
    {
        var owner = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var accounts = await AccountsAsync(owner);
        var labor = accounts["2.2"].GetProperty("id").GetGuid();
        Used[labor] = true;

        var child = await owner.PostAsJsonAsync("/api/v1/finance/chart-of-accounts", Account("2.2.01", "Montadores", "Expense", "Costs", labor));
        child.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(child)).GetProperty("detail").GetString().Should().Be("Esta conta já tem lançamentos e não pode virar agrupadora. Crie a conta em outro grupo.");
        var delete = await owner.DeleteAsync($"/api/v1/finance/chart-of-accounts/{labor}");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(delete);
        problem.GetProperty("code").GetString().Should().Be("account_in_use");
        problem.GetProperty("detail").GetString().Should().Be("Conta com lançamentos não pode ser excluída. Inative-a.");
        (await owner.PostAsync($"/api/v1/finance/chart-of-accounts/{labor}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var root = accounts["2"].GetProperty("id").GetGuid();
        var synthetic = await owner.PostAsync($"/api/v1/finance/chart-of-accounts/{root}/inactivate", null);
        synthetic.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(synthetic)).GetProperty("detail").GetString().Should().StartWith("Inative antes as contas filhas: 2.1, 2.3");

        await using var scope = await ScopeAsync();
        var chart = scope.ServiceProvider.GetRequiredService<IChartOfAccounts>();
        var postable = await chart.PostableAsync(AccountType.Expense);
        postable.Should().NotContain(a => a.Id == labor || a.Code == "2" || a.Code == "3", "só analíticas ativas (CA 17)");
        postable.Should().Contain(a => a.Code == "3.9");
        (await chart.FindAsync([labor])).Should().ContainSingle(a => !a.IsActive, "por id, até inativa");
    }

    [Fact]
    public async Task Centros_de_custo_com_as_mesmas_regras()
    {
        var owner = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var centers = (await JsonAsync(await owner.GetAsync("/api/v1/finance/cost-centers"))).EnumerateArray().ToDictionary(c => c.GetProperty("code").GetString()!, c => c.GetProperty("id").GetGuid());
        var admin = centers["1"];
        (await JsonAsync(await owner.GetAsync($"/api/v1/finance/cost-centers/suggestion?parentId={admin}"))).GetProperty("code").GetString().Should().Be("1.1");
        var accounting = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/finance/cost-centers", new { code = "1.1", name = "Contabilidade", parentId = admin }));
        (await owner.PostAsJsonAsync("/api/v1/finance/cost-centers", new { code = "1.1", name = "Repetido", parentId = admin })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(await owner.DeleteAsync($"/api/v1/finance/cost-centers/{admin}"))).GetProperty("code").GetString().Should().Be("cost_center_has_children");
        Used[accounting] = true;
        (await JsonAsync(await owner.DeleteAsync($"/api/v1/finance/cost-centers/{accounting}"))).GetProperty("detail").GetString().Should().Be("Centro de custo com lançamentos não pode ser excluído. Inative-o.");
        var cycle = await owner.PutAsJsonAsync($"/api/v1/finance/cost-centers/{admin}", new { code = "1.1.01", name = "Administrativo", parentId = accounting });
        (await cycle.Content.ReadAsStringAsync()).Should().Contain("O centro de custo não pode ficar dentro dele mesmo.");

        var finance = await ActorAsync("finance");
        (await finance.DeleteAsync($"/api/v1/finance/cost-centers/{centers["2"]}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "finance.delete só no Proprietário");
        var nobody = await ActorAsync();
        (await nobody.GetAsync("/api/v1/finance/chart-of-accounts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
