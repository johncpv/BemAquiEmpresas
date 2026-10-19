using System.Net;
using System.Net.Http.Json;
using BemAqui.Finance.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

public partial class ChartTests
{
    [Fact]
    public async Task Seed_v1_em_arvore_com_centros_e_sem_duplicar_nem_voltar_ao_padrao()
    {
        var owner = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var accounts = await AccountsAsync(owner);
        accounts.Keys.Should().Contain(["1", "1.9", "2.5", "3.9", "4.1", "5.2"]);
        accounts["1"].GetProperty("acceptsEntries").GetBoolean().Should().BeFalse("raiz com filhas é sintética");
        accounts["1.9"].GetProperty("acceptsEntries").GetBoolean().Should().BeTrue();
        accounts["2.5"].GetProperty("dreGroup").GetString().Should().Be("Deductions");
        accounts["5.2"].GetProperty("type").GetString().Should().Be("Liability");
        accounts["4"].GetProperty("dreGroup").GetString().Should().Be("OutsideDre");
        (await JsonAsync(await owner.GetAsync("/api/v1/finance/cost-centers"))).EnumerateArray().Select(c => c.GetProperty("name").GetString())
            .Should().Contain(["Administrativo", "Comercial", "Produção", "Instalação"]);

        var renamed = accounts["1.1"].GetProperty("id").GetGuid();
        (await owner.PutAsJsonAsync($"/api/v1/finance/chart-of-accounts/{renamed}", Account("1.1", "Venda de elevadores", "Revenue", "GrossRevenue", accounts["1"].GetProperty("id").GetGuid()))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/api/v1/finance/chart-of-accounts/{accounts["1.4"].GetProperty("id").GetGuid()}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using (var scope = await ScopeAsync())
        {
            await new ChartOfAccountsSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
            await new CostCentersSeeder().SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }

        var again = await AccountsAsync(owner);
        again.Should().NotContainKey("1.4", "o excluído não volta (CA 2)");
        again["1.1"].GetProperty("name").GetString().Should().Be("Venda de elevadores", "o editado não volta ao padrão");
        await using var check = await ScopeAsync();
        (await check.ServiceProvider.GetRequiredService<IFinanceDb>().Accounts.IgnoreQueryFilters().CountAsync(a => a.SeedKey != null)).Should().Be(ChartOfAccountsSeeder.Seeds.Count);
        (await check.ServiceProvider.GetRequiredService<IFinanceDb>().CostCenters.CountAsync(c => c.SeedKey != null)).Should().Be(4);
    }

    [Fact]
    public async Task Conta_filha_com_codigo_sugerido_vira_o_pai_sintetico_e_valida_mascara_tipo_e_codigo()
    {
        var finance = await ActorAsync("finance");
        var accounts = await AccountsAsync(finance);
        var materials = accounts["2.1"].GetProperty("id").GetGuid();
        var suggestion = await JsonAsync(await finance.GetAsync($"/api/v1/finance/chart-of-accounts/suggestion?parentId={materials}"));
        suggestion.GetProperty("code").GetString().Should().Be("2.1.01");
        suggestion.GetProperty("type").GetString().Should().Be("Expense");
        suggestion.GetProperty("dreGroup").GetString().Should().Be("Costs");

        await CreatedAsync(await finance.PostAsJsonAsync("/api/v1/finance/chart-of-accounts", Account("2.1.01", "Chapas de aço", "Expense", "Costs", materials)));
        (await AccountsAsync(finance))["2.1"].GetProperty("acceptsEntries").GetBoolean().Should().BeFalse("ganhou a primeira filha (CA 9)");
        (await JsonAsync(await finance.GetAsync($"/api/v1/finance/chart-of-accounts/suggestion?parentId={materials}"))).GetProperty("code").GetString().Should().Be("2.1.02");

        var mask = await finance.PostAsJsonAsync("/api/v1/finance/chart-of-accounts", Account("2.1.1", "Errada", "Expense", "Costs", materials));
        mask.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await mask.Content.ReadAsStringAsync()).Should().Contain("Use o formato 9.9.99.");
        var type = await finance.PostAsJsonAsync("/api/v1/finance/chart-of-accounts", Account("2.1.05", "Tipo errado", "Revenue", "GrossRevenue", materials));
        (await type.Content.ReadAsStringAsync()).Should().Contain("A conta precisa ter o mesmo tipo da conta pai.");
        var dup = await finance.PostAsJsonAsync("/api/v1/finance/chart-of-accounts", Account("2.1.01", "Repetida", "Expense", "Costs", materials));
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(dup)).GetProperty("detail").GetString().Should().Be("Já existe a conta 2.1.01.");

        var freight = accounts["2.4"].GetProperty("id").GetGuid();
        var parallel = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => finance.PostAsJsonAsync("/api/v1/finance/chart-of-accounts", Account("2.4.01", "Frete de chapa", "Expense", "Costs", freight))));
        parallel.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "o índice único garante (CA 8)");

        var root3 = accounts["3"].GetProperty("id").GetGuid();
        var cycle = await finance.PutAsJsonAsync($"/api/v1/finance/chart-of-accounts/{root3}", Account("3.1.01", "Despesas", "Expense", "OperatingExpenses", accounts["3.1"].GetProperty("id").GetGuid()));
        (await cycle.Content.ReadAsStringAsync()).Should().Contain("A conta não pode ficar dentro dela mesma.");
    }
}
