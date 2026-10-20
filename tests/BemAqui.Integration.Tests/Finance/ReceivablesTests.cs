using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Commercial.Contracts;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-01: títulos a receber, parcelas, baixa, quitação, estorno, cancelamento, limites e relatório.</summary>
public partial class ReceivablesTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    /// <summary>Contratos cancelados para o CA 20 (o <c>IContracts</c> real nasce na COM-04a).</summary>
    private static readonly ConcurrentDictionary<Guid, bool> CancelledContracts = new();

    private sealed class FakeContracts : IContracts
    {
        public Task<bool?> IsCancelledAsync(Guid contractId, CancellationToken cancellationToken = default) =>
            Task.FromResult<bool?>(CancelledContracts.ContainsKey(contractId) ? true : null);

        public Task<ContractInfo?> FindAsync(Guid contractId, CancellationToken cancellationToken = default) => Task.FromResult<ContractInfo?>(null);
    }

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IContracts, FakeContracts>()));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> QueryAsync<T>(Func<TenantDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync();
        return await query(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    /// <summary>Usuário num grupo do seed (Proprietário ou "finance"), num grupo só com <c>finance.view</c> ("view") ou sem grupo.</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "r" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey == "view")
            {
                var group = new Group { Name = "Consulta " + Guid.NewGuid().ToString("N")[..6], Description = "Só vê" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "finance", Enabled = true });
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "finance", Action = "view" });
                groupId = group.Id;
            }
            else
            {
                groupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync();
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = groupId });
            await db.SaveChangesAsync();
        }

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    private static HttpRequestMessage Post(string url, object body, Guid? key = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", (key ?? Guid.NewGuid()).ToString());
        return request;
    }

    private static async Task<Guid> CustomerAsync(HttpClient owner, string role = "Customer")
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Cliente " + Guid.NewGuid().ToString("N")[..6], roles = new[] { role } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private Task<Guid> RevenueAsync(string code = "1.1") => QueryAsync(db => db.Accounts.Where(a => a.Code == code).Select(a => a.Id).SingleAsync());

    private static async Task<Guid> BankAsync(HttpClient owner, string? openingDate = null)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Banco " + Guid.NewGuid().ToString("N")[..6], kind = "Bank", bankCode = "077", agency = "1234-5", account = "12345678-9", openingBalance = openingDate is null ? (decimal?)null : 1000m, openingDate, isDefault = false });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    /// <summary>Título manual de <paramref name="amount"/>, vencendo em <paramref name="due"/>; devolve o JSON do título.</summary>
    private async Task<JsonElement> ReceivableAsync(HttpClient client, Guid party, decimal amount = 1000m, DateOnly? due = null)
    {
        var body = new { partyId = party, description = "Venda de teste", accountId = await RevenueAsync(), costCenterId = (Guid?)null, projectId = (Guid?)null, amount, issueDate = Iso(Today.AddDays(-30)), dueDate = Iso(due ?? Today.AddDays(10)) };
        var created = await client.SendAsync(Post("/api/v1/finance/receivables", body));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("items")[0];
    }

    private static object Settle(Guid bank, decimal principal, uint version, decimal interest = 0, decimal fine = 0, decimal discount = 0, DateOnly? date = null, string? reason = null) =>
        new { bankAccountId = bank, date = Iso(date ?? Today), principal, interest, fine, discount, backdateReason = reason, version };

    private static uint Version(JsonElement receivable) => receivable.GetProperty("version").GetUInt32();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;
}
