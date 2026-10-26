using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Identity;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Domain;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-01a: painel do proprietário com o dataset fixo das stories de origem (FIN-04/05, FIN-06a, COM-08). Cada card é comparado
/// com o relatório de origem no mesmo dia (CA 6). Banco desta classe; os testes filtram pelos próprios ids quando dividem tabela.
/// </summary>
public partial class DashboardTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string HostA = "a.localhost";
    private const string HostB = "b.localhost";
    private const string Base = "/api/v1/dashboard";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string? Code(JsonElement problem) => problem.TryGetProperty("code", out var code) ? code.GetString() : null;

    private async Task<AsyncServiceScope> ScopeAsync(Tenant? tenant = null)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync((tenant ?? tenancy.TenantA).Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Usuário num grupo do seed (Proprietário, Financeiro, Comercial) ou sem grupo, com token de teste (sem 2FA).</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey, Tenant? tenant = null)
    {
        var t = tenant ?? tenancy.TenantA;
        var (id, stamp) = await tenancy.SeedUserAsync(t, "d" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync(t);
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        return (id, ClientFor(t, id, stamp));
    }

    /// <summary>Usuário num grupo novo só com as ações pedidas (ex.: dashboard.view e quotes.view).</summary>
    private async Task<(Guid Id, HttpClient Client, Guid GroupId)> ActorWithAsync(params string[] permissions)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
        var group = new Group { Name = "Painel " + Guid.NewGuid().ToString("N")[..8], Description = "Teste do painel" };
        db.Groups.Add(group);
        foreach (var module in permissions.Select(p => p.Split('.')[0]).Distinct())
        {
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
        }

        foreach (var permission in permissions)
        {
            db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = permission.Split('.')[0], Action = permission.Split('.')[1] });
        }

        db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
        await db.SaveChangesAsync();
        return (id, ClientFor(tenancy.TenantA, id, stamp), group.Id);
    }

    private HttpClient ClientFor(Tenant tenant, Guid id, string stamp)
    {
        var host = tenant.Id == tenancy.TenantB.Id ? HostB : HostA;
        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenant.Id, host, userId: id, stamp: stamp));
        return client;
    }

    private static async Task<JsonElement> OkAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url + ": " + await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static async Task<Guid> CustomerAsync(HttpClient owner, string name)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = name, roles = new[] { "Customer" } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static Receivable Rec(Guid party, Guid account, decimal amount, DateOnly? due, ReceivableStatus status = ReceivableStatus.Open) => new()
    {
        Number = "DSH-" + Guid.NewGuid().ToString("N")[..10], PartyId = party, AccountId = account, Description = "Venda", Amount = amount, IssueDate = Today.AddDays(-200), DueDate = due,
        Status = status, Origin = DocumentOrigin.Manual, CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Dataset fixo (o mesmo desenho do FIN-06a e do COM-08): duas contas com saldo inicial e movimento, títulos vencidos em três
    /// faixas e orçamentos em todas as situações. Chamado uma vez por banco; devolve os ids para filtrar.
    /// </summary>
    private async Task<Dataset> DatasetAsync(HttpClient owner, Guid ownerId, Tenant? tenant = null)
    {
        var (alfa, beta) = (await CustomerAsync(owner, "Alfa Condomínio " + Guid.NewGuid().ToString("N")[..4]), await CustomerAsync(owner, "Beta Engenharia " + Guid.NewGuid().ToString("N")[..4]));
        var tag = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return await WithDbAsync(async db =>
        {
            var account = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var bank = new BankAccount { Name = "Inter " + tag, Kind = BankAccountKind.Bank, OpeningBalance = 196_000m, OpeningDate = Today.AddDays(-60), IsActive = true };
            var cash = new BankAccount { Name = "Caixa " + tag, Kind = BankAccountKind.Cash, OpeningBalance = 18_000m, OpeningDate = Today.AddDays(-60), IsActive = true };
            db.BankAccounts.AddRange(bank, cash);
            db.FinancialTransactions.AddRange(
                new FinancialTransaction { BankAccountId = bank.Id, Date = Today.AddDays(-10), Amount = 110.55m, Type = TransactionType.BankEntry, Description = "Entrada", CreatedAt = DateTimeOffset.UtcNow },
                new FinancialTransaction { BankAccountId = cash.Id, Date = Today.AddDays(-3), Amount = 270.10m, Type = TransactionType.BankEntry, Description = "Entrada", CreatedAt = DateTimeOffset.UtcNow });
            var partial = Rec(alfa, account, 500m, Today.AddDays(-35), ReceivableStatus.PartiallyPaid);
            var largest = Rec(beta, account, 74_670.33m, Today.AddDays(-9));
            db.Receivables.AddRange(Rec(alfa, account, 1_000m, Today.AddDays(-5)), partial, largest, Rec(beta, account, 2_000.01m, Today.AddDays(-70)),
                Rec(beta, account, 999m, null, ReceivableStatus.Planned), Rec(alfa, account, 300m, Today.AddDays(5)));
            db.Entry(partial).Property(r => r.PaidAmount).CurrentValue = 200m;

            var inPeriod = DateTimeOffset.UtcNow.AddDays(-10);
            Quote Q(string suffix, QuoteStatus status, decimal price, DateOnly? validUntil = null) => new()
            {
                Number = $"DSH-{tag}-{suffix}", Revision = 0, IsCurrent = true, Status = status, PartyId = alfa, TotalPrice = price, TotalCost = price / 2,
                CreatedAt = inPeriod, CreatedBy = ownerId, LostReason = status == QuoteStatus.Rejected ? QuoteLostReason.Price : null, LostAt = status == QuoteStatus.Rejected ? inPeriod : null,
                ValidUntil = validUntil,
            };
            db.Quotes.AddRange(Q("D1", QuoteStatus.Draft, 100m), Q("S1", QuoteStatus.Sent, 1_000m, Today.AddDays(3)), Q("S2", QuoteStatus.Sent, 2_500.50m, Today.AddDays(30)),
                Q("A1", QuoteStatus.Approved, 3_000m), Q("L1", QuoteStatus.Rejected, 500m), Q("E1", QuoteStatus.Expired, 1_000m));
            await db.SaveChangesAsync();
            return new Dataset(bank.Id, cash.Id, largest.Id, beta);
        }, tenant);
    }

    private sealed record Dataset(Guid BankId, Guid CashId, Guid LargestReceivableId, Guid LargestParty);

    [Fact]
    public async Task Cada_card_e_igual_ao_relatorio_de_origem_centavo_a_centavo()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var data = await DatasetAsync(owner, ownerId);

        // Saldo em contas = Contas e extrato (FIN-04/05).
        var balances = await OkAsync(owner, Base + "/cards/balances");
        var report = await OkAsync(owner, "/api/v1/finance/bank-accounts/balances");
        balances.GetProperty("consolidated").GetDecimal().Should().Be(report.GetProperty("consolidated").GetDecimal());
        var byAccount = report.GetProperty("accounts").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetGuid(), a => a.GetProperty("currentBalance").GetDecimal());
        foreach (var account in balances.GetProperty("accounts").EnumerateArray())
        {
            account.GetProperty("balance").GetDecimal().Should().Be(byAccount[account.GetProperty("id").GetGuid()]);
        }

        byAccount[data.BankId].Should().Be(196_110.55m);
        byAccount[data.CashId].Should().Be(18_270.10m);

        // A receber vencido = Inadimplência (FIN-06a).
        var overdue = await OkAsync(owner, Base + "/cards/overdue");
        var aging = await OkAsync(owner, "/api/v1/finance/aging");
        overdue.GetProperty("total").GetDecimal().Should().Be(aging.GetProperty("total").GetDecimal()).And.BeGreaterThanOrEqualTo(77_970.34m, "1.000 + 300 + 74.670,33 + 2.000,01 deste teste");
        overdue.GetProperty("count").GetInt32().Should().Be(aging.GetProperty("count").GetInt32());
        overdue.GetProperty("customers").GetInt32().Should().Be(aging.GetProperty("customers").GetInt32());
        overdue.GetProperty("buckets").EnumerateArray().Select(b => b.GetProperty("total").GetDecimal())
            .Should().Equal(aging.GetProperty("buckets").EnumerateArray().Select(b => b.GetProperty("total").GetDecimal()));
        overdue.GetProperty("topCustomers").EnumerateArray().Select(c => (c.GetProperty("partyId").GetGuid(), c.GetProperty("total").GetDecimal()))
            .Should().Equal(aging.GetProperty("rows").EnumerateArray().Take(5).Select(r => (r.GetProperty("partyId").GetGuid(), r.GetProperty("total").GetDecimal())));
        overdue.GetProperty("maxDaysLate").GetInt32().Should().Be(aging.GetProperty("rows").EnumerateArray().Max(r => r.GetProperty("maxDaysLate").GetInt32()));
        overdue.GetProperty("largest").GetProperty("receivableId").GetGuid().Should().Be(data.LargestReceivableId);
        overdue.GetProperty("largest").GetProperty("daysLate").GetInt32().Should().Be(9);

        // Comercial = Funil (COM-08), mesmo período padrão de 90 dias.
        var commercial = await OkAsync(owner, Base + "/cards/commercial");
        var funnel = await OkAsync(owner, "/api/v1/quotes/funnel");
        commercial.GetProperty("from").GetString().Should().Be(funnel.GetProperty("from").GetString());
        var stages = funnel.GetProperty("stages").EnumerateArray().ToDictionary(s => s.GetProperty("status").GetString()!, s => (Count: s.GetProperty("count").GetInt32(), Amount: s.GetProperty("amount").GetDecimal()));
        commercial.GetProperty("stages").EnumerateArray().ToDictionary(s => s.GetProperty("status").GetString()!, s => (Count: s.GetProperty("count").GetInt32(), Amount: s.GetProperty("amount").GetDecimal()))
            .Should().BeEquivalentTo(stages);
        commercial.GetProperty("openAmount").GetDecimal().Should().Be(stages["Draft"].Amount + stages["Sent"].Amount);
        commercial.GetProperty("conversionByCount").GetDecimal().Should().Be(funnel.GetProperty("conversion").GetProperty("byCount").GetDecimal());
        commercial.GetProperty("expiringCount").GetInt32().Should().Be(funnel.GetProperty("expiring").GetArrayLength());

        // Cabeçalho: saudação, frase-resumo e a faixa Atenção com o maior vencido levando ao título.
        var overview = await OkAsync(owner, Base);
        overview.GetProperty("greeting").GetString().Should().BeOneOf("Bom dia", "Boa tarde", "Boa noite");
        overview.GetProperty("summary").GetString().Should().Contain("a receber vencido");
        var alert = overview.GetProperty("attention").EnumerateArray().Single(a => a.GetProperty("code").GetString() == "overdue");
        alert.GetProperty("url").GetString().Should().Be("/app/financeiro/receber/" + data.LargestReceivableId);
        overview.GetProperty("cards").EnumerateArray().Select(c => c.GetString()).Should().ContainInOrder("balances", "cash-forecast", "decisions", "revenue", "overdue", "commercial");
    }

    [Fact]
    public async Task Caixa_projetado_e_faturamento_sao_os_numeros_da_FIN_07_e_da_FIN_08()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        await DatasetAsync(owner, ownerId);
        await owner.PostAsync(Base + "/refresh", null);

        // Caixa projetado (CA 6): a série semanal e os horizontes são os do ICashForecast, a mesma conta do fluxo de caixa.
        var cash = await OkAsync(owner, Base + "/cards/cash-forecast");
        await using (var scope = await ScopeAsync())
        {
            var forecast = scope.ServiceProvider.GetRequiredService<ICashForecast>();
            var weeks = await forecast.WeeksAsync(84);
            cash.GetProperty("available").GetBoolean().Should().BeTrue();
            cash.GetProperty("currentBalance").GetDecimal().Should().Be(weeks.CurrentBalance);
            cash.GetProperty("weeks").EnumerateArray().Select(w => (w.GetProperty("baseBalance").GetDecimal(), w.GetProperty("adjustedBalance").GetDecimal()))
                .Should().Equal(weeks.Weeks.Select(w => (w.BaseBalance, w.AdjustedBalance)));
            cash.GetProperty("difference").GetDecimal().Should().Be(weeks.LowestDifference);
            if (weeks.AdjustedLowest is { } lowest)
            {
                cash.GetProperty("lowestAdjusted").GetProperty("balance").GetDecimal().Should().Be(lowest.Balance);
            }

            foreach (var horizon in cash.GetProperty("horizons").EnumerateArray())
            {
                var summary = await forecast.SummaryAsync(horizon.GetProperty("days").GetInt32());
                horizon.GetProperty("baseBalance").GetDecimal().Should().Be(summary.Base.EndBalance);
                horizon.GetProperty("adjustedBalance").GetDecimal().Should().Be(summary.Adjusted.EndBalance);
            }

            // Faturamento (CA 6): a linha Receita bruta do DRE pelo IRevenueSummary, mês e ano até o mês, com o provisório.
            var revenue = await OkAsync(owner, Base + "/cards/revenue");
            var end = new DateOnly(Today.Year, Today.Month, DateTime.DaysInMonth(Today.Year, Today.Month));
            var months = await scope.ServiceProvider.GetRequiredService<IRevenueSummary>().GrossRevenueAsync(new DateOnly(Today.Year, 1, 1), end);
            revenue.GetProperty("monthAmount").GetDecimal().Should().Be(months.Where(m => m.Month == Today.Month).Sum(m => m.Amount));
            revenue.GetProperty("yearAmount").GetDecimal().Should().Be(months.Sum(m => m.Amount));
            revenue.GetProperty("provisional").GetBoolean().Should().Be(months.Any(m => m.IsProvisional));
        }
    }

    [Fact]
    public async Task Card_de_faturamento_mostra_a_meta_os_dias_uteis_e_o_link_so_para_quem_tem_settings_edit()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var months = Enumerable.Repeat<decimal?>(380_000m, 12).ToArray();
        var saved = await owner.PutAsJsonAsync($"/api/v1/finance/revenue-targets/{Today.Year}", new { months });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        await owner.PostAsync(Base + "/refresh", null);

        var revenue = await OkAsync(owner, Base + "/cards/revenue");
        revenue.GetProperty("monthTarget").GetDecimal().Should().Be(380_000m);
        revenue.GetProperty("yearTarget").GetDecimal().Should().Be(4_560_000m);
        revenue.GetProperty("canEditTargets").GetBoolean().Should().BeTrue();
        revenue.GetProperty("available").GetBoolean().Should().BeTrue();
        var end = new DateOnly(Today.Year, Today.Month, DateTime.DaysInMonth(Today.Year, Today.Month));
        var expected = 0;
        for (var d = Today; d <= end; d = d.AddDays(1))
        {
            expected += d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 0 : 1;
        }

        revenue.GetProperty("workingDaysLeft").GetInt32().Should().Be(expected, "calendário padrão sem feriado: segunda a sexta (CAD-03)");

        var (_, finance) = await ActorAsync("finance");
        (await OkAsync(finance, Base + "/cards/revenue")).GetProperty("canEditTargets").GetBoolean().Should().BeFalse("sem settings.edit o link Definir meta não aparece");
    }

    [Fact]
    public async Task Metas_gravam_com_auditoria_antes_e_depois_e_sem_settings_edit_dao_403()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var year = Today.Year + 1;
        var first = new decimal?[12];
        first[0] = 100_000.004m;
        var created = await owner.PutAsJsonAsync($"/api/v1/finance/revenue-targets/{year}", new { months = first });
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        var body = await JsonAsync(created);
        body.GetProperty("months")[0].GetProperty("amount").GetDecimal().Should().Be(100_000.00m, "numeric(18,2)");
        body.GetProperty("months")[1].GetProperty("amount").ValueKind.Should().Be(JsonValueKind.Null);

        first[0] = 120_000m;
        (await owner.PutAsJsonAsync($"/api/v1/finance/revenue-targets/{year}", new { months = first })).StatusCode.Should().Be(HttpStatusCode.OK);
        var negative = (decimal?[])first.Clone();
        negative[3] = -1m;
        var invalid = await owner.PutAsJsonAsync($"/api/v1/finance/revenue-targets/{year}", new { months = negative });
        invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await owner.PutAsJsonAsync($"/api/v1/finance/revenue-targets/{year}", new { months = new decimal?[3] })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "são 12 meses");

        var audit = await WithDbAsync(async db =>
        {
            var target = await db.RevenueTargets.AsNoTracking().SingleAsync(t => t.Year == year && t.Month == 1);
            var id = target.Id.ToString();
            return await db.AuditEntries.AsNoTracking().Where(a => a.EntityId == id).OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync();
        });
        audit.Select(a => a.Action).Should().Equal("Created", "Updated");
        audit[1].Changes.Should().Contain("100000").And.Contain("120000", "antes e depois");

        var (_, finance) = await ActorAsync("finance");
        (await finance.GetAsync($"/api/v1/finance/revenue-targets/{year}")).StatusCode.Should().Be(HttpStatusCode.OK, "o Financeiro vê a meta");
        var denied = await finance.PutAsJsonAsync($"/api/v1/finance/revenue-targets/{year}", new { months = first });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "gravar exige settings.edit (CA 9)");
        Code(await JsonAsync(denied)).Should().Be("forbidden");
    }
}
