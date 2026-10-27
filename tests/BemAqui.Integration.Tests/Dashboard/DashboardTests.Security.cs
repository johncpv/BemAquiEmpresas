using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Commercial.Contracts;
using BemAqui.Dashboard.Application;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Platform.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-01a CA 3, 4, 5, 12, 13, 15 e 16: card por permissão e módulo do dono, três camadas, cache por usuário e versão de
/// permissão com geração por evento, "Atualizar" com rate limit, dois tenants e a página inicial do Proprietário.
/// </summary>
public partial class DashboardTests
{
    private static readonly string[] FinanceCards = ["balances", "cash-forecast", "revenue", "overdue"];

    private async Task SetReleasedAsync(string module, bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == module)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    private async Task<decimal> AddTransactionAsync(Guid bankAccountId, decimal amount) =>
        await WithDbAsync(async db =>
        {
            db.FinancialTransactions.Add(new FinancialTransaction { BankAccountId = bankAccountId, Date = Today, Amount = amount, Type = TransactionType.BankEntry, Description = "Teste do cache", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            return amount;
        });

    private async Task<Guid> BankAsync(decimal opening, Tenant? tenant = null) =>
        await WithDbAsync(async db =>
        {
            var bank = new BankAccount { Name = "Cache " + Guid.NewGuid().ToString("N")[..6], Kind = BankAccountKind.Bank, OpeningBalance = opening, OpeningDate = Today.AddDays(-1), IsActive = true };
            db.BankAccounts.Add(bank);
            await db.SaveChangesAsync();
            return bank.Id;
        }, tenant);

    private static async Task<decimal> BalanceOfAsync(HttpClient client, Guid bankId) =>
        (await OkAsync(client, Base + "/cards/balances")).GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == bankId).GetProperty("balance").GetDecimal();

    [Fact]
    public async Task Card_sem_a_permissao_do_dono_nao_e_montado_e_o_endpoint_da_403()
    {
        // Comercial do seed não tem dashboard.view: o painel some (CA 16).
        var (_, commercialSeed) = await ActorAsync("commercial");
        var denied = await commercialSeed.GetAsync(Base);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(denied)).Should().Be("forbidden");

        // dashboard.view + quotes.view: só o Comercial; os do Financeiro não existem nem chamados direto (CA 4 e 5).
        var (_, sales, _) = await ActorWithAsync("dashboard.view", "quotes.view");
        var overview = await OkAsync(sales, Base);
        overview.GetProperty("cards").EnumerateArray().Select(c => c.GetString()).Should().Equal("commercial");
        overview.GetProperty("summary").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null, "a frase só usa cards visíveis");
        foreach (var card in FinanceCards)
        {
            var response = await sales.GetAsync($"{Base}/cards/{card}");
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, card);
            Code(await JsonAsync(response)).Should().Be("forbidden", card);
        }

        (await sales.GetAsync(Base + "/cards/decisions")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem nenhuma ação de decisão");
        (await sales.GetAsync(Base + "/cards/commercial")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Financeiro do seed: dashboard.view e finance.view, sem aprovar nada (CA 5).
        var (_, finance) = await ActorAsync("finance");
        (await OkAsync(finance, Base)).GetProperty("cards").EnumerateArray().Select(c => c.GetString()).Should().Contain(FinanceCards).And.Contain("commercial").And.NotContain("decisions");

        // Só dashboard.view: nenhum card (a tela mostra "Você ainda não tem acesso aos indicadores").
        var (_, bare, _) = await ActorWithAsync("dashboard.view");
        (await OkAsync(bare, Base)).GetProperty("cards").GetArrayLength().Should().Be(0);
        (await bare.GetAsync(Base + "/cards/commercial")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Tres_camadas_modulo_do_painel_e_do_dono_nao_liberados()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await SetReleasedAsync("finance", false);
        try
        {
            var card = await owner.GetAsync(Base + "/cards/balances");
            card.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(card)).Should().Be("module_not_released", "o card responde com o código da camada do dono (CA 4)");
            (await OkAsync(owner, Base)).GetProperty("cards").EnumerateArray().Select(c => c.GetString()).Should().NotContain(FinanceCards);
        }
        finally
        {
            await SetReleasedAsync("finance", true);
        }

        await SetReleasedAsync("dashboard", false);
        try
        {
            foreach (var url in new[] { Base, Base + "/cards/balances", Base + "/cards/commercial" })
            {
                var response = await owner.GetAsync(url);
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
                Code(await JsonAsync(response)).Should().Be("module_not_released", url);
            }
        }
        finally
        {
            await SetReleasedAsync("dashboard", true);
        }

        // Cada endpoint declara o módulo do painel e dashboard.view; os cards, também o RequireModule e a permissão do dono.
        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/dashboard", StringComparison.Ordinal) == true)
            // O painel do supervisor (DSH-03) é tela da Fábrica: exige production, não dashboard (SupervisorPanelTests).
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/dashboard/supervisor", StringComparison.Ordinal) != true).ToList();
        endpoints.Should().HaveCountGreaterThanOrEqualTo(8);
        foreach (var endpoint in endpoints)
        {
            var modules = endpoint.Metadata.GetOrderedMetadata<RequiresModuleMetadata>().Select(m => m.ModuleCode).ToList();
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();
            modules.Should().Contain("dashboard", endpoint.DisplayName);
            policies.Should().Contain(ModuleEndpoints.PermissionPolicyPrefix + "dashboard.view", endpoint.DisplayName);
            var card = endpoint.RoutePattern.RawText!.Split('/').Last();
            if (DashboardCards.Find(card) is { } rule && rule.Permissions.Count == 1)
            {
                modules.Should().Contain(CardRule.ModuleOf(rule.Permissions[0]), endpoint.DisplayName);
                policies.Should().Contain(ModuleEndpoints.PermissionPolicyPrefix + rule.Permissions[0], endpoint.DisplayName);
            }
        }
    }

    [Fact]
    public async Task Cache_de_60s_cai_com_evento_com_Atualizar_e_a_perda_de_permissao_vale_na_hora()
    {
        var (id, user, groupId) = await ActorWithAsync("dashboard.view", "finance.view");
        var bank = await BankAsync(1_000m);
        (await BalanceOfAsync(user, bank)).Should().Be(1_000m);

        await AddTransactionAsync(bank, 50m);
        (await BalanceOfAsync(user, bank)).Should().Be(1_000m, "dentro dos 60 s o card vem do cache");

        // Evento que muda os números: o consumidor sobe a geração do tenant e o painel inteiro recalcula (CA 12).
        await using (var scope = await ScopeAsync())
        {
            await scope.ServiceProvider.GetRequiredService<DashboardGenerationConsumer>()
                .HandleAsync(new ReceivableSettled(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Today, Today, 50m, false), TestContext.Current.CancellationToken);
        }

        (await BalanceOfAsync(user, bank)).Should().Be(1_050m);

        // "Atualizar" ignora o cache do usuário; o segundo clique em menos de 10 s dá 429 (CA 13).
        await AddTransactionAsync(bank, 25m);
        (await BalanceOfAsync(user, bank)).Should().Be(1_050m);
        (await user.PostAsync(Base + "/refresh", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await BalanceOfAsync(user, bank)).Should().Be(1_075m);
        var limited = await user.PostAsync(Base + "/refresh", null);
        limited.StatusCode.Should().Be((HttpStatusCode)429);
        limited.Headers.Contains("Retry-After").Should().BeTrue();
        (await limited.Content.ReadAsStringAsync()).Should().Contain("Aguarde alguns segundos para atualizar de novo.");
        var (_, other, _) = await ActorWithAsync("dashboard.view", "finance.view");
        (await other.PostAsync(Base + "/refresh", null)).StatusCode.Should().Be(HttpStatusCode.NoContent, "o limite é por usuário");

        // Perda de permissão: a versão muda e o card some na hora, mesmo com o cache quente.
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            await db.GroupPermissions.Where(g => g.GroupId == groupId && g.ModuleCode == "finance").ExecuteDeleteAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionService>().BumpAsync([id]);
        }

        (await user.GetAsync(Base + "/cards/balances")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await OkAsync(user, Base)).GetProperty("cards").EnumerateArray().Select(c => c.GetString()).Should().NotContain("balances");
    }

    [Fact]
    public async Task Dois_tenants_e_dois_usuarios_com_cache_quente_cada_um_ve_o_seu()
    {
        var (_, ownerA) = await ActorAsync(Group.OwnerSeedKey);
        var (_, ownerB) = await ActorAsync(Group.OwnerSeedKey, tenancy.TenantB);
        var bankA = await BankAsync(111.11m);
        var bankB = await BankAsync(222.22m, tenancy.TenantB);

        for (var round = 0; round < 2; round++)
        {
            var a = await OkAsync(ownerA, Base + "/cards/balances");
            var b = await OkAsync(ownerB, Base + "/cards/balances");
            a.GetProperty("accounts").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().Contain(bankA).And.NotContain(bankB);
            b.GetProperty("accounts").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Should().Contain(bankB).And.NotContain(bankA);
        }

        // Mesmo tenant, permissões diferentes, cache quente do Proprietário: quem só vê o Comercial continua sem o saldo.
        var (_, sales, _) = await ActorWithAsync("dashboard.view", "quotes.view");
        (await sales.GetAsync(Base + "/cards/balances")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var commercialOwner = await OkAsync(ownerA, Base + "/cards/commercial");
        var commercialSales = await OkAsync(sales, Base + "/cards/commercial");
        commercialSales.GetProperty("openAmount").GetDecimal().Should().Be(commercialOwner.GetProperty("openAmount").GetDecimal(), "o número é o mesmo; a chave do cache é que é por usuário");
        commercialSales.GetRawText().Should().NotContainAny("cost", "Cost", "margin", "Margin");
    }

    [Fact]
    public async Task Pagina_inicial_Proprietario_vai_ao_painel_e_o_Financeiro_ao_Inicio()
    {
        // O front decide pelo /me/permissions (homeRouteFor): Proprietário com o painel liberado → /app/painel; o resto → /app.
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var me = await OkAsync(owner, "/api/v1/me/permissions");
        me.GetProperty("isOwner").GetBoolean().Should().BeTrue();
        me.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("code").GetString() == "dashboard").GetProperty("released").GetBoolean().Should().BeTrue();

        var (_, finance) = await ActorAsync("finance");
        var financeMe = await OkAsync(finance, "/api/v1/me/permissions");
        financeMe.GetProperty("isOwner").GetBoolean().Should().BeFalse();
        financeMe.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Contain("dashboard.view", "o painel continua no dock do Financeiro");
    }
}
