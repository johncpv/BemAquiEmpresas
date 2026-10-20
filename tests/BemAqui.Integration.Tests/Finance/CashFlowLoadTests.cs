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

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-05 CA 12: 50 mil títulos e 20 mil transações, fluxo de 90 dias em menos de 2 s (banco desta classe).</summary>
public class CashFlowLoadTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    [Fact]
    public async Task Fluxo_de_90_dias_com_50_mil_titulos_responde_em_menos_de_2s()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        Guid userId;
        string stamp;
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var bank = new BankAccount { Name = "Carga", Kind = BankAccountKind.Bank, OpeningBalance = 0m, IsActive = true };
            db.BankAccounts.Add(bank);
            await db.SaveChangesAsync();
            var revenue = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var expense = await db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync();
            var party = Guid.CreateVersion7();
            // Carga de teste direto no banco (só neste banco de teste), parâmetros nos ids.
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.receivables (id, number, party_id, description, account_id, amount, paid_amount, interest, fine, discount, issue_date, due_date, status, origin, created_at)
                SELECT gen_random_uuid(), 'L' || g, {party}, 'Carga', {revenue}, 100, 0, 0, 0, 0, current_date - 60, current_date + (g % 400) - 30,
                       CASE WHEN g % 3 = 0 THEN 'Paid' ELSE 'Open' END, 'Manual', now() FROM generate_series(1, 30000) g
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.payables (id, number, party_id, description, account_id, amount, retention_iss, retention_inss, retention_ir, retention_pcc, paid_amount, issue_date, due_date, is_estimate, status, origin, created_at)
                SELECT gen_random_uuid(), 'P' || g, {party}, 'Carga', {expense}, 90, 0, 0, 0, 0, 0, current_date - 60, current_date + (g % 400) - 30, false,
                       CASE WHEN g % 4 = 0 THEN 'Paid' ELSE 'Open' END, 'Manual', now() FROM generate_series(1, 20000) g
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.financial_transactions (id, bank_account_id, date, amount, type, description, created_at)
                SELECT gen_random_uuid(), {bank.Id}, current_date - (g % 365), CASE WHEN g % 2 = 0 THEN 10 ELSE -7 END, 'BankEntry', 'Carga', now() FROM generate_series(1, 20000) g
                """);
            await db.Database.ExecuteSqlRawAsync("ANALYZE finance.receivables; ANALYZE finance.payables; ANALYZE finance.financial_transactions;");
        }

        (userId, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "carga" + Guid.NewGuid().ToString("N")[..8] + "@teste.com");
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var identity = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            identity.UserGroups.Add(new UserGroup { UserId = userId, GroupId = await identity.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync() });
            await identity.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: userId, stamp: stamp));
        (await client.GetAsync("/api/v1/finance/cash-flow?days=1")).StatusCode.Should().Be(HttpStatusCode.OK, "aquecimento");
        var watch = Stopwatch.StartNew();
        var response = await client.GetAsync("/api/v1/finance/cash-flow?days=90");
        watch.Stop();
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }
}
