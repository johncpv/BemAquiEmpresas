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

namespace BemAqui.Integration.Tests.HR;

/// <summary>CAD-04a: cargos com custo-hora e faixa salarial sensível, jornadas e sindicatos.</summary>
public partial class HrTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<AsyncServiceScope> ScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Usuário num grupo do seed, num grupo com as ações de <c>hr</c> dadas ("hr:view,edit") ou sem grupo.</summary>
    private async Task<HttpClient> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "h" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey.StartsWith("hr:", StringComparison.Ordinal))
            {
                var group = new Group { Name = "RH parcial " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "hr", Enabled = true });
                foreach (var action in seedKey[3..].Split(','))
                {
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "hr", Action = action });
                }

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
        return client;
    }

    private static async Task<Guid> SeedScheduleAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().First(s => s.GetProperty("seedKey").GetString() == "44h-seg-sex").GetProperty("id").GetGuid();

    private static object Position(string name, Guid? scheduleId, decimal? monthlyHours = null) => new { name, cbo = "7244-15", defaultWorkCenterId = (Guid?)null, workScheduleId = scheduleId, monthlyHours };

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..6];
}
