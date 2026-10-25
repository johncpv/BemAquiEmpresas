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

/// <summary>
/// FIN-05 CA 12, FIN-06a CA 5 e FIN-07 CA 16: 50 mil títulos de 2 mil clientes e 20 mil transações; fluxo de 90 dias com os dois
/// cenários em menos de 2 s, sem N+1 na pontualidade, e aging em menos de 1 s.
/// </summary>
public class CashFlowLoadTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    [Fact]
    public async Task Fluxo_de_90_dias_com_50_mil_titulos_responde_em_menos_de_2s()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        Guid userId;
        Guid bankId;
        string stamp;
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var bank = new BankAccount { Name = "Carga", Kind = BankAccountKind.Bank, OpeningBalance = 0m, IsActive = true };
            db.BankAccounts.Add(bank);
            await db.SaveChangesAsync();
            bankId = bank.Id;
            var revenue = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
            var expense = await db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync();
            var party = Guid.CreateVersion7();
            // Carga de teste direto no banco (só neste banco de teste), parâmetros nos ids.
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO finance.receivables (id, number, party_id, description, account_id, amount, paid_amount, interest, fine, discount, issue_date, due_date, status, origin, created_at)
                SELECT gen_random_uuid(), 'L' || g, md5('cliente' || (g % 2000))::uuid, 'Carga', {revenue}, 100, CASE WHEN g % 3 = 0 THEN 100 ELSE 0 END, 0, 0, 0, current_date - 60, current_date + (g % 400) - 30,
                       CASE WHEN g % 3 = 0 THEN 'Paid' ELSE 'Open' END, 'Manual', now() FROM generate_series(1, 30000) g
                """);
            // FIN-07 CA 16: uma baixa por título quitado, com atraso de 0 a 20 dias, para a pontualidade dos 2 mil clientes.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO finance.receivable_settlements (id, receivable_id, date, principal, interest, fine, discount, total, balance_before, kind, created_at)
                SELECT gen_random_uuid(), r.id, LEAST(current_date, r.due_date + (abs(hashtext(r.number)) % 21)), r.amount, 0, 0, 0, r.amount, r.amount, 'Settlement', now()
                FROM finance.receivables r WHERE r.number LIKE 'L%' AND r.status = 'Paid'
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
            await db.Database.ExecuteSqlRawAsync("ANALYZE finance.receivables; ANALYZE finance.receivable_settlements; ANALYZE finance.payables; ANALYZE finance.financial_transactions;");
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
        // FIN-07 CA 16: comandos SQL contados pela fonte de rastreamento do Npgsql; N+1 daria um por cliente (2 mil).
        var commands = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ => Interlocked.Increment(ref commands),
        };
        ActivitySource.AddActivityListener(listener);
        var watch = Stopwatch.StartNew();
        var response = await client.GetAsync("/api/v1/finance/cash-flow?days=90&scenario=both");
        watch.Stop();
        var executed = Volatile.Read(ref commands);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
        executed.Should().BeLessThan(60, "a pontualidade dos 2 mil clientes vem em lote (ICustomerPaymentStats.ForPartiesAsync)");

        // FIN-06a CA 5: o aging com a mesma carga em menos de 1 s.
        watch.Restart();
        var aging = await client.GetAsync("/api/v1/finance/aging");
        watch.Stop();
        aging.StatusCode.Should().Be(HttpStatusCode.OK, await aging.Content.ReadAsStringAsync());
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));

        // FIN-04 CA 17: primeira página do extrato de um ano (20 mil transações) em menos de 1 s.
        watch.Restart();
        var statement = await client.GetAsync($"/api/v1/finance/bank-accounts/{bankId}/statement?from={DateTime.UtcNow.AddDays(-370):yyyy-MM-dd}&to={DateTime.UtcNow:yyyy-MM-dd}");
        watch.Stop();
        statement.StatusCode.Should().Be(HttpStatusCode.OK, await statement.Content.ReadAsStringAsync());
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}
