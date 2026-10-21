using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Time;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Finance;

/// <summary>FIN-03b: alçadas multinível. Banco desta classe (as regras mudam todas as OPs).</summary>
public partial class ApprovalRuleTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/finance/payment-orders";

    private static DateOnly Today => BusinessTime.Today(TimeProvider.System);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static uint Version(JsonElement order) => order.GetProperty("version").GetUInt32();

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "o" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
            await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([id]);
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

    private static async Task<Guid> SupplierAsync(HttpClient owner)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = (string?)null, legalName = "Fornecedor " + Guid.NewGuid().ToString("N")[..6], roles = new[] { "Supplier" } });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    /// <summary>Título de 1.000,00 com ISS de 50,00 (líquido 950,00), pago por Pix com CPF.</summary>
    private async Task<JsonElement> PayableAsync(HttpClient client, Guid party, bool withMethod = true)
    {
        var account = await WithDbAsync(db => db.Accounts.Where(a => a.Code == "3.3").Select(a => a.Id).SingleAsync());
        var pix = new { kind = "Pix", pixKeyType = "Cpf", pixKey = "52998224725", holderName = "Fulano de Tal", holderDocument = "52998224725" };
        var body = new { partyId = party, description = "Serviço", accountId = account, costCenterId = (Guid?)null, amount = 1_000m, issueDate = Iso(Today.AddDays(-5)), dueDate = Iso(Today.AddDays(3)),
            retentionIss = 50m, retentionInss = 0m, retentionIr = 0m, retentionPcc = 0m, paymentDetails = withMethod ? pix : null };
        var created = await client.SendAsync(Post("/api/v1/finance/payables", body));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("items")[0];
    }

    private static object Items(params (Guid Id, decimal Amount)[] items) => new { items = items.Select(i => new { payableId = i.Id, amount = i.Amount, expectedCharges = 0m }), expectedDate = (string?)null };

    private static async Task<Guid> BankAsync(HttpClient owner)
    {
        var created = await owner.PostAsJsonAsync("/api/v1/finance/bank-accounts", new { name = "Banco " + Guid.NewGuid().ToString("N")[..6], kind = "Bank", bankCode = "077", agency = "1234-5", account = "12345678-9", openingBalance = 5_000m, openingDate = Iso(Today.AddDays(-30)), isDefault = false });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }
}
