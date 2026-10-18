using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Parties;

/// <summary>CAD-01a: cadastro de terceiros com documento único, papéis, endereços, contatos, busca e LGPD.</summary>
public partial class PartiesTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Cnpj = "47225005000186";
    private const string CnpjAlpha = "12ABC34501DE35";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private HttpClient Client(string token)
    {
        var client = tenancy.ClientFor(Host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Usuário do tenant A num grupo do seed (pela seed_key) ou sem grupo.</summary>
    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "p" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        return (id, Client(TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp)));
    }

    private static object Company(string? document, string name, params string[] roles) =>
        new { kind = "PJ", document, legalName = name, tradeName = (string?)null, roles = roles.Length == 0 ? ["Customer"] : roles };

    private static async Task<Guid> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    /// <summary>CNPJ válido e único por teste (14 dígitos com DV calculado).</summary>
    private static string NewCnpj()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("12345678").Take(12).Select(c => c - '0').ToList();
        int Dv(int[] w) { var r = w.Select((x, i) => x * digits[i]).Sum() % 11; return r < 2 ? 0 : 11 - r; }
        digits.Add(Dv([5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        digits.Add(Dv([6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]));
        return string.Concat(digits);
    }

    /// <summary>CPF válido e único por teste.</summary>
    private static string NewCpf()
    {
        var digits = Guid.NewGuid().ToString("N").Where(char.IsAsciiDigit).Concat("98765432").Take(9).Select(c => c - '0').ToList();
        int Dv(int size) { var r = digits.Take(size).Select((x, i) => x * (size + 1 - i)).Sum() * 10 % 11; return r == 10 ? 0 : r; }
        digits.Add(Dv(9));
        digits.Add(Dv(10));
        return string.Concat(digits);
    }
}
