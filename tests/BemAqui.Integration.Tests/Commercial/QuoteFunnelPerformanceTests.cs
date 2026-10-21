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

/// <summary>COM-08 CA 7: funil com 50 mil orçamentos em menos de 1 s. Banco próprio, para não pesar nas outras classes.</summary>
public class QuoteFunnelPerformanceTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    [Fact]
    public async Task Funil_com_50_mil_orcamentos_em_menos_de_1_segundo()
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "f" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            identity.UserGroups.Add(new UserGroup { UserId = id, GroupId = await identity.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync() });
            await identity.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);

            // 50 mil números, revisão 0 corrente, situações e motivos espalhados, criados nos últimos 400 dias.
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
            var party = Guid.NewGuid();
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO commercial.quotes (id, number, revision, is_current, status, party_id, pricing_mode, total_cost, total_price, valid_until, lost_reason, created_at, created_by, is_deleted)
                SELECT gen_random_uuid(), 'PERF-' || g, 0, true,
                       (ARRAY['Draft','Sent','Rejected','Expired','Approved'])[1 + g % 5], {party}, 'Markup', 100, 100 + g % 1000,
                       CURRENT_DATE + (g % 30), CASE WHEN g % 5 = 2 THEN (ARRAY['Price','Deadline','Competitor','CustomerGaveUp','NoAnswer','Other'])[1 + g % 6] END,
                       now() - make_interval(days => g % 400), {id}, false
                FROM generate_series(1, 50000) AS g
                """);
            await db.Database.ExecuteSqlRawAsync("ANALYZE commercial.quotes");
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        (await client.GetAsync("/api/v1/quotes/funnel")).StatusCode.Should().Be(HttpStatusCode.OK, "aquecimento");

        var watch = Stopwatch.StartNew();
        var response = await client.GetAsync($"/api/v1/quotes/funnel?authorId={id}");
        watch.Stop();
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        watch.ElapsedMilliseconds.Should().BeLessThan(1_000);
    }
}
