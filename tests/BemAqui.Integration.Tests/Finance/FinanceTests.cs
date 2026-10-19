using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Finance.Application;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>CAD-05b: contas bancárias e caixas, condições de pagamento e condição padrão do terceiro.</summary>
public partial class FinanceTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    /// <summary>Contas "com movimentação" para os CAs 5 e 8 (o provedor real vem na FIN-01).</summary>
    private static readonly ConcurrentDictionary<Guid, bool> Moved = new();

    private sealed class FakeMovement : IBankAccountUsageProvider
    {
        public Task<bool> HasMovementAsync(Guid accountId, CancellationToken cancellationToken = default) => Task.FromResult(Moved.ContainsKey(accountId));
    }

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IBankAccountUsageProvider, FakeMovement>()));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Usuário do tenant A num grupo do seed (pela seed_key), num grupo só com <c>finance.view</c> ("view") ou sem grupo.</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "f" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey == "view")
            {
                var group = new Group { Name = "Consulta financeira " + Guid.NewGuid().ToString("N")[..6], Description = "Só vê" };
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

    private static object Bank(string name, bool isDefault = false, decimal? balance = null, string? date = null, string agency = "1234-5", string account = "12345678-9") =>
        new { name, kind = "Bank", bankCode = "077", agency, account, openingBalance = balance, openingDate = date, isDefault };

    private static async Task<Guid> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<T> QueryAsync<T>(Func<BemAqui.Host.Persistence.TenantDbContext, Task<T>> query)
    {
        await using var scope = await ScopeAsync();
        return await query(scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>());
    }
}
