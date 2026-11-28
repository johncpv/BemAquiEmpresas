using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Host.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

/// <summary>
/// EXE-02: modelos de etapas do projeto de serviço. O tenant de teste nasce só com <c>factory</c> (como a Konebras); cada teste liga ou desliga o
/// perfil Serviços conforme precisa. Banco desta classe.
/// </summary>
public partial class StageTemplateTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/stage-templates";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static uint Version(JsonElement e) => e.GetProperty("version").GetUInt32();

    private async Task<T> WithDbAsync<T>(Func<TenantDbContext, Task<T>> action)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        return await action(scope.ServiceProvider.GetRequiredService<TenantDbContext>());
    }

    private async Task<(Guid Id, HttpClient Client)> ActorAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "m" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
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

    /// <summary>Usuário num grupo só com <c>projects.view</c>.</summary>
    private async Task<HttpClient> ViewerAsync()
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "v" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await TenantScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "Projetos " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
            db.Groups.Add(group);
            db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = "projects", Enabled = true });
            db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = "projects", Action = "view" });
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    private async Task<AsyncServiceScope> TenantScopeAsync()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Liga ou desliga o perfil Serviços no tenant A.</summary>
    private async Task ServicesAsync(bool on)
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Application.Modules.TenantModuleService>();
        var result = on
            ? await service.ApplyProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Services, null, null)
            : await service.RemoveProfileAsync(tenancy.TenantA.Id, BusinessProfileCatalog.Services, null, null);
        result.IsSuccess.Should().BeTrue();
        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    /// <summary>Roda os pacotes do perfil como a aba "Perfis" (PERF-04).</summary>
    private async Task SeedProfilesAsync()
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Application.Modules.TenantProfileSeeding>().RunAsync(tenancy.TenantA.Id)).Should().BeTrue();
    }

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..6];

    private static object Step(string code, string role, decimal hours = 2m, Guid? positionId = null, Guid? id = null, string? name = null) =>
        new { id, name = name ?? "Etapa " + code, code, role, standardHoursPerUnit = hours, positionId, requiresApproval = false, checklist = Array.Empty<string>() };

    private static object[] Steps(Guid? positionId = null) =>
        [Step("K", "Execution", 4m, positionId), Step("G", "Delivery", 1m), Step("A", "Acceptance", 0.5m)];

    private static readonly object[] Milestones =
        [new { percent = 50m, trigger = "Signature", stageCode = (string?)null, daysAfterEvent = 0 }, new { percent = 50m, trigger = "StageCompleted", stageCode = "K", daysAfterEvent = 5 }];

    private static async Task<JsonElement> CreateAsync(HttpClient client, string name, string? segment = null, object[]? steps = null)
    {
        var created = await client.PostAsJsonAsync(Base, new { name, segment, steps = steps ?? Steps(), suggestedMilestones = Milestones });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return await JsonAsync(created);
    }

    /// <summary>Cargo e item direto no banco (os cadastros têm os próprios testes).</summary>
    private Task<Guid> PositionAsync(bool active = true) => WithDbAsync(async db =>
    {
        var position = new BemAqui.HR.Domain.JobPosition { Name = "Consultor " + Guid.NewGuid().ToString("N")[..4], MonthlyHours = 220, HourlyCost = 80m, HourlyCostManual = true, IsActive = active };
        db.JobPositions.Add(position);
        await db.SaveChangesAsync();
        return position.Id;
    });

    private Task<Guid> ItemAsync(BemAqui.Inventory.Contracts.ItemType type) => WithDbAsync(async db =>
    {
        var unit = await db.Units.Select(u => u.Id).FirstAsync();
        var item = new BemAqui.Inventory.Domain.Item { Type = type, Code = "SRV-" + Guid.NewGuid().ToString("N")[..5], Name = "Implantação", UnitId = unit, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    });
}
