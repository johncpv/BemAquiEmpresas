using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-01a CA 14: com 50 mil títulos, 20 mil transações, 50 mil orçamentos e 2 mil clientes, o painel sem cache chega em menos de
/// 2 s (cada card em menos de 1,5 s) e com cache em menos de 300 ms. Banco próprio; o aquecimento (JIT e planos) é feito com
/// outro usuário, e a medição usa um usuário novo, então o cache dele começa frio.
/// </summary>
[Trait(LoadTest.Trait, LoadTest.Value)]
public class DashboardPerformanceTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static readonly string[] Urls =
    [
        "/api/v1/dashboard", "/api/v1/dashboard/cards/balances", "/api/v1/dashboard/cards/cash-forecast", "/api/v1/dashboard/cards/decisions",
        "/api/v1/dashboard/cards/revenue", "/api/v1/dashboard/cards/overdue", "/api/v1/dashboard/cards/commercial",
    ];

    private async Task<HttpClient> OwnerAsync(ITenantScopeFactory scopes, TenantInfo info)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "perf" + Guid.NewGuid().ToString("N")[..8] + "@teste.com");
        await using (var scope = await scopes.CreateScopeAsync(info))
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            identity.UserGroups.Add(new UserGroup { UserId = id, GroupId = await identity.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync() });
            await identity.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    /// <summary>Todos os cards ao mesmo tempo, como a tela pede; devolve o tempo total e o de cada um.</summary>
    private static async Task<(TimeSpan Total, Dictionary<string, TimeSpan> ByCard)> LoadAsync(HttpClient client)
    {
        var total = Stopwatch.StartNew();
        var results = await Task.WhenAll(Urls.Select(async url =>
        {
            var watch = Stopwatch.StartNew();
            var response = await client.GetAsync(url);
            watch.Stop();
            response.StatusCode.Should().Be(HttpStatusCode.OK, url + ": " + await response.Content.ReadAsStringAsync());
            return (url, watch.Elapsed);
        }));
        total.Stop();
        return (total.Elapsed, results.ToDictionary(r => r.url, r => r.Elapsed));
    }

    [Fact]
    public async Task Painel_com_a_carga_chega_em_menos_de_2s_sem_cache_e_300ms_com_cache()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        var scopes = tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>();
        await using (var scope = await scopes.CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
            var bank = new BankAccount { Name = "Carga do painel", Kind = BankAccountKind.Bank, OpeningBalance = 0m, IsActive = true };
            db.BankAccounts.Add(bank);
            await db.SaveChangesAsync();
            var revenue = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            // Carga direto no banco (só neste banco de teste): 2 mil clientes com id derivado do número, para ligar títulos e orçamentos.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO parties.parties (id, kind, document, legal_name, trade_name, search_text, tax_regime, is_active, created_at, is_deleted)
                SELECT md5('dsh-' || g)::uuid, 'PJ', NULL, 'Cliente ' || g, NULL, 'cliente ' || g, 'NotInformed', true, now(), false FROM generate_series(0, 1999) g;
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.receivables (id, number, party_id, description, account_id, amount, paid_amount, interest, fine, discount, issue_date, due_date, status, origin, created_at)
                SELECT gen_random_uuid(), 'DSHP' || g, md5('dsh-' || (g % 2000))::uuid, 'Carga', {revenue}, 100 + g % 50, 0, 0, 0, 0, current_date - 120, current_date + (g % 400) - 200,
                       CASE WHEN g % 3 = 0 THEN 'Paid' ELSE 'Open' END, 'Manual', now() FROM generate_series(1, 50000) g
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.financial_transactions (id, bank_account_id, date, amount, type, description, created_at)
                SELECT gen_random_uuid(), {bank.Id}, current_date - (g % 365), CASE WHEN g % 2 = 0 THEN 10 ELSE -7 END, 'BankEntry', 'Carga', now() FROM generate_series(1, 20000) g
                """);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO commercial.quotes (id, number, revision, is_current, status, party_id, pricing_mode, total_cost, total_price, valid_until, lost_reason, created_at, created_by, is_deleted)
                SELECT gen_random_uuid(), 'DSHQ-' || g, 0, true, (ARRAY['Draft','Sent','Rejected','Expired','Approved'])[1 + g % 5], md5('dsh-' || (g % 2000))::uuid, 'Markup', 100, 100 + g % 1000,
                       CURRENT_DATE + (g % 30), CASE WHEN g % 5 = 2 THEN 'Price' END, now() - make_interval(days => g % 400), NULL, false
                FROM generate_series(1, 50000) AS g;
                ANALYZE parties.parties; ANALYZE finance.receivables; ANALYZE finance.financial_transactions; ANALYZE commercial.quotes;
                """);
        }

        await LoadAsync(await OwnerAsync(scopes, info)); // aquecimento com outro usuário

        var owner = await OwnerAsync(scopes, info);
        var cold = await LoadAsync(owner);
        cold.Total.Should().BeLessThan(TimeSpan.FromSeconds(2), "sem cache (CA 14): " + string.Join(", ", cold.ByCard.Select(c => $"{c.Key} {c.Value.TotalMilliseconds:0} ms")));
        cold.ByCard.Values.Should().OnlyContain(t => t < TimeSpan.FromSeconds(1.5), "cada card em menos de 1,5 s");

        var warm = await LoadAsync(owner);
        warm.Total.Should().BeLessThan(TimeSpan.FromMilliseconds(300), "com cache (CA 14): " + string.Join(", ", warm.ByCard.Select(c => $"{c.Key} {c.Value.TotalMilliseconds:0} ms")));
    }
}
