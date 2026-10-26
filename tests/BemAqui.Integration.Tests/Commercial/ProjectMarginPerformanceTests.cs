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

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// DSH-02 CA 18: 5 mil projetos, 200 mil linhas na razão e 50 mil títulos a pagar com projeto. Metas: primeira página do relatório em menos
/// de 1 s, consolidado em menos de 2 s e aba Custos de um projeto em menos de 300 ms. A suíte roda sob carga, então o teste afere com 50% de
/// folga (1,5 s, 3 s e 450 ms). Banco próprio, para não pesar nas outras classes.
/// </summary>
public class ProjectMarginPerformanceTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    [Fact]
    public async Task Relatorio_com_5_mil_projetos_responde_dentro_da_meta()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        Guid sample;
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(3));
            var revenue = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var expense = await db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync();
            var party = Guid.CreateVersion7();
            // Carga de teste direto no banco (só neste banco de teste), parâmetros nos ids.
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
                INSERT INTO commercial.projects (id, number, contract_id, party_id, name, status, created_at, delivered_manually, fabricated_by_orders)
                SELECT gen_random_uuid(), 'PP' || substring(c.number from 3), c.id, {party}, 'Projeto ' || c.number,
                       (ARRAY['Planned','InProgress','Fabricated','Installing'])[1 + substring(c.number from 3)::int % 4], now(), false, false
                FROM commercial.contracts c WHERE c.number LIKE 'PC%'
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
                       CASE WHEN s % 4 IN (2, 3) THEN -15 ELSE 120 END, CURRENT_DATE - (s % 60), now(), false
                FROM commercial.projects p CROSS JOIN generate_series(1, 40) AS s
                WHERE p.number LIKE 'PP%'
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.payables (id, number, party_id, description, account_id, project_id, cost_type, amount, retention_iss, retention_inss, retention_ir, retention_pcc, paid_amount,
                                              issue_date, due_date, is_estimate, status, origin, installment, created_at)
                SELECT gen_random_uuid(), 'PX' || substring(p.number from 3) || '-' || s, {party}, 'Carga', {expense}, p.id,
                       (ARRAY['Installation','Freight','Material','Other','Tax'])[1 + s % 5], 300, 0, 0, 0, 0, 0, CURRENT_DATE - 30, CURRENT_DATE + s, s % 3 = 0,
                       CASE WHEN s % 3 = 0 THEN 'Planned' ELSE 'Open' END, (ARRAY['Manual','Installer','Purchase'])[1 + s % 3], 1, now()
                FROM commercial.projects p CROSS JOIN generate_series(1, 10) AS s
                WHERE p.number LIKE 'PP%'
                """);
            await db.Database.ExecuteSqlRawAsync("ANALYZE commercial.projects; ANALYZE commercial.contracts; ANALYZE commercial.project_budget; ANALYZE commercial.project_costs; ANALYZE finance.payables;");
            (await db.ProjectCosts.CountAsync()).Should().BeGreaterThanOrEqualTo(200_000);
            (await db.Payables.CountAsync(p => p.ProjectId != null)).Should().BeGreaterThanOrEqualTo(50_000);
            sample = await db.Projects.Where(p => p.Number == "PP2500").Select(p => p.Id).SingleAsync();
        }

        var (userId, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "margem" + Guid.NewGuid().ToString("N")[..8] + "@teste.com");
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            identity.UserGroups.Add(new UserGroup { UserId = userId, GroupId = await identity.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync() });
            await identity.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([userId]);
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: userId, stamp: stamp));
        (await client.GetAsync("/api/v1/projects/margins/ranking?pageSize=1")).StatusCode.Should().Be(HttpStatusCode.OK, "aquecimento");

        // Melhor de três medições (como na pauta, DSH-04 CA 12): absorve o ruído das classes pesadas que rodam em paralelo na suíte.
        var best = TimeSpan.MaxValue;
        var watch = new Stopwatch();
        for (var i = 0; i < 3; i++)
        {
            watch.Restart();
            var ranking = await client.GetAsync("/api/v1/projects/margins/ranking");
            watch.Stop();
            ranking.StatusCode.Should().Be(HttpStatusCode.OK, await ranking.Content.ReadAsStringAsync());
            best = watch.Elapsed < best ? watch.Elapsed : best;
        }

        best.Should().BeLessThan(TimeSpan.FromMilliseconds(1_500), "meta de 1 s com folga");

        watch.Restart();
        var portfolio = await client.GetAsync("/api/v1/projects/margins/portfolio");
        watch.Stop();
        portfolio.StatusCode.Should().Be(HttpStatusCode.OK, await portfolio.Content.ReadAsStringAsync());
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "meta de 2 s com folga");

        (await client.GetAsync($"/api/v1/projects/{sample}/costs")).StatusCode.Should().Be(HttpStatusCode.OK, "aquecimento");
        watch.Restart();
        var costs = await client.GetAsync($"/api/v1/projects/{sample}/costs");
        watch.Stop();
        costs.StatusCode.Should().Be(HttpStatusCode.OK, await costs.Content.ReadAsStringAsync());
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(450), "meta de 300 ms com folga");
    }
}
