using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Platform.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-02: títulos a pagar, retenções, forma de pagamento, trava em OP, IPayables e despesas recorrentes.</summary>
public partial class PayablesTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private async Task<AsyncServiceScope> ScopeAsync(Tenant? tenant = null)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync((tenant ?? tenancy.TenantA).Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    private async Task<T> QueryAsync<T>(Func<TenantDbContext, Task<T>> query, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        return await query(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
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

        var client = tenancy.Factory.CreateClient();
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

    /// <summary>Fornecedor PJ com CNPJ válido (para o aviso do titular).</summary>
    private static async Task<(Guid Id, string Document)> SupplierAsync(HttpClient owner, string role = "Supplier")
    {
        var document = NewCnpj();
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document, legalName = "Fornecedor " + Guid.NewGuid().ToString("N")[..6], roles = new[] { role } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return ((await JsonAsync(created)).GetProperty("id").GetGuid(), document);
    }

    private Task<Guid> AccountAsync(string code = "3.3") => QueryAsync(db => db.Accounts.Where(a => a.Code == code).Select(a => a.Id).SingleAsync());

    private async Task<object> BodyAsync(Guid party, decimal amount = 1000m, decimal iss = 0, object? payment = null, DateOnly? due = null) => new
    {
        partyId = party, description = "Serviço de teste", accountId = await AccountAsync(), costCenterId = (Guid?)null, amount, issueDate = Iso(Today.AddDays(-5)), dueDate = Iso(due ?? Today.AddDays(10)),
        retentionIss = iss, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, paymentDetails = payment,
    };

    private async Task<JsonElement> PayableAsync(HttpClient client, Guid party, decimal amount = 1000m, decimal iss = 0, object? payment = null)
    {
        var created = await client.SendAsync(Post("/api/v1/finance/payables", await BodyAsync(party, amount, iss, payment)));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await JsonAsync(created);
    }

    private static uint Version(JsonElement payable) => payable.GetProperty("version").GetUInt32();

    private static string NewCnpj()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("12345678").Take(8).Select(c => c - '0').Concat([0, 0, 0, 1]).ToList();
        int Dv(int[] weights) { var r = digits.Select((x, i) => x * weights[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        digits.Add(Dv([5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        digits.Add(Dv([6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(digits);
    }
}
