using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Finance.Contracts;
using BemAqui.Finance.Domain;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-05: fluxo de caixa com dataset fixo (banco desta classe), valores conferidos centavo a centavo.</summary>
public partial class CashFlowTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/finance/cash-flow";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<HttpClient> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "f" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
            await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private sealed record Dataset(Guid BankA, Guid BankB, Guid Overdue, Guid Estimate);

    private static Receivable Rec(Guid party, Guid account, string number, decimal amount, DateOnly? due, ReceivableStatus status = ReceivableStatus.Open, DocumentOrigin origin = DocumentOrigin.Manual) => new()
    {
        Number = number, PartyId = party, AccountId = account, Description = "Venda " + number, Amount = amount, IssueDate = Today.AddDays(-60), DueDate = due, Status = status, Origin = origin, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Payable Pay(Guid party, Guid account, string number, decimal amount, DateOnly? due, PayableStatus status = PayableStatus.Open) => new()
    {
        Number = number, PartyId = party, AccountId = account, Description = "Compra " + number, Amount = amount, IssueDate = Today.AddDays(-60), DueDate = due, Status = status, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static FinancialTransaction Tx(Guid bank, int day, decimal amount) => new()
    {
        BankAccountId = bank, Date = Today.AddDays(day), Amount = amount, Type = amount > 0 ? TransactionType.Receipt : TransactionType.BankEntry, Description = "Movimento " + day, CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>2 contas, 6 transações (1 futura), 5 a receber em aberto, 1 cancelado, 1 sem data; 5 a pagar, 1 estimado, 1 sem data; 1 recorrente.</summary>
    private Task<Dataset> SeedAsync() => WithDbAsync(async db =>
    {
        var party = Guid.CreateVersion7();
        var revenue = await db.Accounts.Where(a => a.Code == "1.1").Select(a => a.Id).SingleAsync();
        var expense = await db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync();
        var a = new BankAccount { Name = "Banco A", Kind = BankAccountKind.Bank, OpeningBalance = 10_000m, OpeningDate = Today.AddDays(-30), IsActive = true };
        var b = new BankAccount { Name = "Caixa B", Kind = BankAccountKind.Cash, OpeningBalance = 2_000m, OpeningDate = Today.AddDays(-30), IsActive = true };
        db.BankAccounts.AddRange(a, b);
        db.FinancialTransactions.AddRange(Tx(a.Id, -5, 1_000m), Tx(a.Id, -3, -500m), Tx(a.Id, 0, 200m), Tx(a.Id, 2, 999m), Tx(b.Id, -1, -300m), Tx(b.Id, -10, 100m));
        var overdue = Rec(party, revenue, "T-R2", 2_000m, Today.AddDays(-3));
        var partial = Rec(party, revenue, "T-R3", 1_500m, Today.AddDays(10), ReceivableStatus.PartiallyPaid);
        db.Receivables.AddRange(Rec(party, revenue, "T-R1", 1_000m, Today.AddDays(5)), overdue, partial, Rec(party, revenue, "T-R4", 800m, Today.AddDays(40)),
            Rec(party, revenue, "T-R5", 700m, Today.AddDays(5), origin: DocumentOrigin.Opening), Rec(party, revenue, "T-R6", 999m, Today.AddDays(5), ReceivableStatus.Cancelled),
            Rec(party, revenue, "T-R7", 4_000m, null, ReceivableStatus.Planned));
        db.Entry(partial).Property(r => r.PaidAmount).CurrentValue = 500m;
        var estimate = Pay(party, expense, "T-P5", 1_200m, Today.AddDays(15), PayableStatus.Planned);
        estimate.IsEstimate = true;
        var withIss = Pay(party, expense, "T-P1", 1_000m, Today.AddDays(5));
        withIss.RetentionIss = 50m;
        var paidPart = Pay(party, expense, "T-P4", 200m, Today.AddDays(5), PayableStatus.PartiallyPaid);
        paidPart.PaidAmount = 50m;
        var recurring = new RecurringExpense { Description = "Aluguel", PartyId = party, AccountId = expense, Amount = 3_000m, DueDay = 10, Start = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-2), IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        db.RecurringExpenses.Add(recurring);
        var next = NextTenth();
        var generated = Pay(party, expense, "T-P6", 3_000m, next);
        generated.RecurringExpenseId = recurring.Id;
        generated.Competence = new DateOnly(next.Year, next.Month, 1);
        generated.Origin = DocumentOrigin.Recurring;
        db.Payables.AddRange(withIss, Pay(party, expense, "T-P2", 3_000m, Today.AddDays(-1)), Pay(party, expense, "T-P3", 500m, Today.AddDays(20)), paidPart, estimate, generated,
            Pay(party, expense, "T-P7", 777m, null, PayableStatus.Planned));
        await db.SaveChangesAsync();
        return new Dataset(a.Id, b.Id, overdue.Id, estimate.Id);
    });

    private static DateOnly NextTenth()
    {
        var d = Today;
        while (d.Day != 10)
        {
            d = d.AddDays(1);
        }

        return d;
    }
}
