using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>
/// DSH-01b CA 10: 5 mil projetos, 200 mil custos de projeto (razão), 50 mil títulos a pagar e 5 mil a receber com projeto e 300 OS. Metas: o
/// painel completo (DSH-01a + DSH-01b) sem cache em menos de 2 s, cada card em menos de 1,5 s, e o resultado por projeto de 12 meses em menos de
/// 2 s. A suíte roda sob carga (como no DSH-02 CA 18), então o teste afere a melhor de três medições, cada uma com um usuário novo (cache frio),
/// com 50% de folga. Banco próprio.
/// </summary>
[Trait(LoadTest.Trait, LoadTest.Value)]
public class OwnerPanelPerformanceTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static readonly string[] Urls =
    [
        "/api/v1/dashboard", "/api/v1/dashboard/cards/balances", "/api/v1/dashboard/cards/cash-forecast", "/api/v1/dashboard/cards/decisions",
        "/api/v1/dashboard/cards/revenue", "/api/v1/dashboard/cards/overdue", "/api/v1/dashboard/cards/commercial", "/api/v1/dashboard/cards/margin",
        "/api/v1/dashboard/cards/active-projects", "/api/v1/dashboard/cards/active-projects/margins", "/api/v1/dashboard/cards/active-projects/received",
        "/api/v1/dashboard/cards/backlog", "/api/v1/dashboard/cards/deliveries", "/api/v1/dashboard/cards/production",
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

    private static async Task<(TimeSpan Total, Dictionary<string, TimeSpan> ByCard)> LoadAsync(HttpClient client, IEnumerable<string> urls)
    {
        var total = Stopwatch.StartNew();
        var results = await Task.WhenAll(urls.Select(async url =>
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
    public async Task Painel_completo_e_resultado_por_projeto_com_5_mil_projetos_dentro_da_meta()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        var scopes = tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>();
        await using (var scope = await scopes.CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
            var revenue = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var expense = await db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync();
            var party = Guid.CreateVersion7();
            // Carga direto no banco (só neste banco de teste), como no DSH-02 CA 18, com previsão, títulos a receber e OS.
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO commercial.quotes (id, number, revision, is_current, status, party_id, pricing_mode, total_cost, total_price, valid_until, created_at, is_deleted)
                SELECT gen_random_uuid(), 'PQ' || g, 0, true, 'Approved', {party}, 'Markup', 0, 100000, CURRENT_DATE, now(), false FROM generate_series(1, 5000) AS g
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO commercial.contracts (id, number, quote_id, party_id, signed_at, total_value, revenue_account_id, status, snapshot, is_onboarding, created_at)
                SELECT gen_random_uuid(), 'PC' || substring(q.number from 3), q.id, {party}, CURRENT_DATE - (substring(q.number from 3)::int % 300), 100000 + substring(q.number from 3)::int % 50000,
                       {revenue}, 'Active', jsonb_build_object(), false, now()
                FROM commercial.quotes q WHERE q.number LIKE 'PQ%'
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO commercial.projects (id, number, contract_id, party_id, name, status, promised_date, created_at, delivered_manually, fabricated_by_orders)
                SELECT gen_random_uuid(), 'PP' || substring(c.number from 3), c.id, {party}, 'Projeto ' || c.number,
                       (ARRAY['Planned','InProgress','Fabricated','Installing'])[1 + substring(c.number from 3)::int % 4], CURRENT_DATE + (substring(c.number from 3)::int % 200) - 50, now(), false, false
                FROM commercial.contracts c WHERE c.number LIKE 'PC%'
                """);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO commercial.project_forecasts (project_id, forecast_date, fabrication_end, computed_at, updated_at)
                SELECT p.id, p.promised_date + (abs(hashtext(p.number)) % 40) - 20, p.promised_date - 10, now(), now() FROM commercial.projects p WHERE p.number LIKE 'PP%'
                """);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO commercial.project_budget (id, project_id, cost_type, budgeted_amount)
                SELECT gen_random_uuid(), p.id, t.cost_type, 5000 + (abs(hashtext(p.number || t.cost_type)) % 20000)
                FROM commercial.projects p CROSS JOIN (VALUES ('Material'), ('Labor'), ('Installation'), ('Freight'), ('Tax')) AS t(cost_type)
                WHERE p.number LIKE 'PP%'
                """);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO commercial.project_costs (id, project_id, cost_type, source, source_id, seq, amount, occurred_on, recorded_at, is_backfill)
                SELECT gen_random_uuid(), p.id, CASE WHEN s % 2 = 0 THEN 'Material' ELSE 'Labor' END,
                       (ARRAY['StockIssue','Labor','StockReturn','LaborCorrection'])[1 + s % 4], gen_random_uuid(), 1,
                       CASE WHEN s % 4 IN (2, 3) THEN -15 ELSE 120 END, CURRENT_DATE - (s * 9 % 365), now(), false
                FROM commercial.projects p CROSS JOIN generate_series(1, 40) AS s
                WHERE p.number LIKE 'PP%'
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.payables (id, number, party_id, description, account_id, project_id, cost_type, amount, retention_iss, retention_inss, retention_ir, retention_pcc, paid_amount,
                                              issue_date, due_date, is_estimate, status, origin, installment, created_at)
                SELECT gen_random_uuid(), 'PX' || substring(p.number from 3) || '-' || s, {party}, 'Carga', {expense}, p.id,
                       (ARRAY['Installation','Freight','Material','Other','Tax'])[1 + s % 5], 300, 0, 0, 0, 0, 0, CURRENT_DATE - (s * 30), CURRENT_DATE + s, s % 3 = 0,
                       CASE WHEN s % 3 = 0 THEN 'Planned' ELSE 'Open' END, (ARRAY['Manual','Installer','Purchase'])[1 + s % 3], 1, now()
                FROM commercial.projects p CROSS JOIN generate_series(1, 10) AS s
                WHERE p.number LIKE 'PP%'
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.receivables (id, number, party_id, description, account_id, project_id, amount, paid_amount, interest, fine, discount, issue_date, due_date, status, origin, created_at)
                SELECT gen_random_uuid(), 'PR' || substring(p.number from 3), {party}, 'Carga', {revenue}, p.id, 20000, 5000, 0, 0, 0, CURRENT_DATE - (abs(hashtext(p.number)) % 330),
                       CURRENT_DATE + 30, 'PartiallyPaid', 'Manual', now()
                FROM commercial.projects p WHERE p.number LIKE 'PP%'
                """);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO production.production_orders (id, number, type, project_id, contract_id, project_item_id, quantity, status, priority, created_at)
                SELECT gen_random_uuid(), 'OS-D-' || row_number() OVER (), 'Fabrication', p.id, p.contract_id, gen_random_uuid(), 1, 'Open', 0, now()
                FROM (SELECT id, contract_id FROM commercial.projects WHERE number LIKE 'PP%' ORDER BY number LIMIT 300) p;
                ANALYZE commercial.projects; ANALYZE commercial.contracts; ANALYZE commercial.project_forecasts; ANALYZE commercial.project_budget; ANALYZE commercial.project_costs;
                ANALYZE finance.payables; ANALYZE finance.receivables; ANALYZE production.production_orders;
                """);
            (await db.ProjectCosts.CountAsync()).Should().BeGreaterThanOrEqualTo(200_000);
            (await db.Projects.CountAsync()).Should().BeGreaterThanOrEqualTo(5_000);
        }

        await LoadAsync(await OwnerAsync(scopes, info), Urls); // aquecimento (JIT e planos) com outro usuário

        var best = (Total: TimeSpan.MaxValue, ByCard: new Dictionary<string, TimeSpan>());
        for (var i = 0; i < 3; i++)
        {
            var cold = await LoadAsync(await OwnerAsync(scopes, info), Urls);
            best = cold.Total < best.Total ? cold : best;
        }

        var detail = string.Join(", ", best.ByCard.Select(c => $"{c.Key} {c.Value.TotalMilliseconds:0} ms"));
        best.Total.Should().BeLessThan(TimeSpan.FromSeconds(3), "meta de 2 s com folga (CA 10): " + detail);
        best.ByCard.Values.Should().OnlyContain(t => t < TimeSpan.FromSeconds(2.25), "cada card em menos de 1,5 s, com folga: " + detail);

        const string results = "/api/v1/dashboard/project-results";
        await LoadAsync(await OwnerAsync(scopes, info), [results]);
        var resultBest = TimeSpan.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var run = await LoadAsync(await OwnerAsync(scopes, info), [results]);
            resultBest = run.Total < resultBest ? run.Total : resultBest;
        }

        resultBest.Should().BeLessThan(TimeSpan.FromSeconds(3), "resultado por projeto de 12 meses com 5 mil projetos: meta de 2 s com folga");
    }
}
