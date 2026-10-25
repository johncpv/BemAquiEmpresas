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

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-08 CA 17: 50 mil títulos a receber, 50 mil a pagar, 100 mil baixas e 24 meses; o DRE mensal em menos de 2 s.</summary>
public class DreLoadTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    [Fact]
    public async Task DRE_de_24_meses_com_200_mil_documentos_responde_em_menos_de_2s()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var revenue = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var expense = await db.Accounts.Where(a => a.Code == "2.1").Select(a => a.Id).SingleAsync();
            var other = await db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync();
            var party = Guid.CreateVersion7();
            // Carga de teste direto no banco (só neste banco de teste), parâmetros nos ids; datas espalhadas pelos últimos 24 meses.
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.receivables (id, number, party_id, description, account_id, amount, paid_amount, interest, fine, discount, issue_date, due_date, status, origin, created_at)
                SELECT gen_random_uuid(), 'LR' || g, {party}, 'Carga', {revenue}, 100, 0, 0, 0, 0, current_date - (g % 720), current_date - (g % 720) + 30,
                       CASE WHEN g % 3 = 0 THEN 'Paid' WHEN g % 50 = 0 THEN 'Cancelled' ELSE 'Open' END, CASE WHEN g % 40 = 0 THEN 'Opening' ELSE 'Manual' END, now()
                FROM generate_series(1, 50000) g
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.payables (id, number, party_id, description, account_id, amount, retention_iss, retention_inss, retention_ir, retention_pcc, paid_amount, issue_date, due_date, is_estimate, status, origin, competence, created_at)
                SELECT gen_random_uuid(), 'LP' || g, {party}, 'Carga', CASE WHEN g % 2 = 0 THEN {expense} ELSE {other} END, 90, 0, 0, 0, 0, 0, current_date - (g % 720), current_date - (g % 720) + 20, false,
                       CASE WHEN g % 4 = 0 THEN 'Paid' WHEN g % 30 = 0 THEN 'Planned' ELSE 'Open' END, CASE WHEN g % 10 = 0 THEN 'Recurring' ELSE 'Manual' END,
                       CASE WHEN g % 10 = 0 THEN date_trunc('month', current_date - (g % 720))::date ELSE NULL END, now()
                FROM generate_series(1, 50000) g
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.receivable_settlements (id, receivable_id, date, principal, interest, fine, discount, total, balance_before, kind, created_at)
                SELECT gen_random_uuid(), r.id, r.issue_date + 10, 100, CASE WHEN s % 5 = 0 THEN 2 ELSE 0 END, 0, CASE WHEN s % 7 = 0 THEN 1 ELSE 0 END, 100, 100, 'Settlement', now()
                FROM (SELECT id, issue_date FROM finance.receivables WHERE number LIKE 'LR%') r CROSS JOIN generate_series(1, 2) s
                """);
            await db.Database.ExecuteSqlRawAsync("ANALYZE finance.receivables; ANALYZE finance.payables; ANALYZE finance.receivable_settlements;");
            (await db.ReceivableSettlements.CountAsync()).Should().BeGreaterThanOrEqualTo(100_000);
        }

        var (userId, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "dreload" + Guid.NewGuid().ToString("N")[..8] + "@teste.com");
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            identity.UserGroups.Add(new UserGroup { UserId = userId, GroupId = await identity.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync() });
            await identity.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: userId, stamp: stamp));
        var today = DateTime.UtcNow;
        var url = $"/api/v1/finance/dre?from={today.AddMonths(-23):yyyy-MM}-01&to={today:yyyy-MM}-01";
        (await client.GetAsync("/api/v1/finance/dre?from=2020-01-01&to=2020-01-01")).StatusCode.Should().Be(HttpStatusCode.OK, "aquecimento");
        var watch = Stopwatch.StartNew();
        var response = await client.GetAsync(url);
        watch.Stop();
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
}
