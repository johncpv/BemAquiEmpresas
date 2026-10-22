using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Engineering.Contracts;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Inventory.Application;
using BemAqui.Inventory.Contracts;
using BemAqui.Platform.Domain;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Engineering;

/// <summary>ENG-01: roteiros de fabricação, vínculo ao produto, seed de exemplo e de permissões, contrato e segurança.</summary>
public partial class RoutingTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private const string Host = "a.localhost";
    private const string Base = "/api/v1/engineering/routing-templates";

    /// <summary>Roteiros "em uso" para o CA 10 (os provedores reais vêm na PRD-01).</summary>
    private static readonly ConcurrentDictionary<Guid, int> InUse = new();

    private sealed class FakeUsage : IRoutingTemplateUsageProvider
    {
        public string Noun => "OS";

        public Task<IReadOnlyDictionary<Guid, int>> CountAsync(IReadOnlyCollection<Guid> templateIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, int>>(templateIds.Where(InUse.ContainsKey).ToDictionary(id => id, id => InUse[id]));
    }

    private WebApplicationFactory<Program>? _factory;

    private WebApplicationFactory<Program> Factory => _factory ??= tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IRoutingTemplateUsageProvider, FakeUsage>()));

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private static string FieldError(JsonElement problem, string field) => problem.GetProperty("errors").GetProperty(field)[0].GetString()!;

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..6];

    private async Task<AsyncServiceScope> ScopeAsync(Tenant? tenant = null)
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync((tenant ?? tenancy.TenantA).Id))!;
        return await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
    }

    /// <summary>Usuário do tenant A num grupo do seed (pela seed_key), num grupo só com <c>{module}.view</c> ("view:módulo") ou sem grupo.</summary>
    private async Task<HttpClient> ActorAsync(string? seedKey = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "r" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            Guid groupId;
            if (seedKey.StartsWith("view:", StringComparison.Ordinal))
            {
                var module = seedKey[5..];
                var group = new Group { Name = "Consulta " + module + " " + Guid.NewGuid().ToString("N")[..6], Description = "Só vê" };
                db.Groups.Add(group);
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = "view" });
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
        return client;
    }

    private Task<HttpClient> EngineerAsync() => ActorAsync("engineering");

    /// <summary>Centro de trabalho ativo (ou inativo) direto pela Produção, com o calendário padrão do tenant.</summary>
    private async Task<Guid> CenterAsync(string name, bool active = true, Tenant? tenant = null)
    {
        await using var scope = await ScopeAsync(tenant);
        var calendar = await scope.ServiceProvider.GetRequiredService<CalendarService>().DefaultEntityAsync();
        var center = new WorkCenter { Name = Unique(name), CalendarId = calendar.Id, IsActive = active, CreatedAt = DateTimeOffset.UtcNow };
        center.SetShifts([new Shift("07:00", "16:00")]);
        var db = scope.ServiceProvider.GetRequiredService<IProductionDb>();
        db.WorkCenters.Add(center);
        await db.SaveChangesAsync();
        return center.Id;
    }

    private async Task<(Guid Id, string Code)> ItemAsync(ItemType type = ItemType.Finished, bool active = true)
    {
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<IInventoryDb>();
        var unit = await db.Units.Where(u => u.Code == "UN").Select(u => u.Id).SingleAsync();
        var item = new BemAqui.Inventory.Domain.Item { Type = type, Code = "PA-" + Guid.NewGuid().ToString("N")[..6], Name = "Cabine padrão", UnitId = unit, Ncm = "84313110", IsActive = active, CreatedAt = DateTimeOffset.UtcNow };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return (item.Id, item.Code);
    }

    private static object Step(string name, string code, Guid? center, decimal hours = 1.5m, bool quality = false, bool approval = false, string[]? checklist = null, Guid? id = null) =>
        new { id, name, code, workCenterId = center, standardHours = hours, isQualityStep = quality, requiresApproval = approval, checklist = checklist ?? [] };

    private static object Template(string name, params object[] steps) => new { name, productType = "Elevador", steps };

    private static async Task<JsonElement> CreatedAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    /// <summary>Roteiro de 3 etapas (Corte, Montagem, Qualidade) em dois centros.</summary>
    private async Task<(JsonElement Template, Guid Cut, Guid Assembly)> ThreeStepsAsync(HttpClient client, string? name = null)
    {
        var cut = await CenterAsync("Corte");
        var assembly = await CenterAsync("Montagem");
        var template = await CreatedAsync(await client.PostAsJsonAsync(Base, Template(name ?? Unique("Cabine"),
            Step("Corte", "C", cut, 2m), Step("Montagem", "M", assembly, 3.25m, checklist: ["Conferir prumo", "Fixar guias"]), Step("Qualidade", "Q", assembly, 0.5m, quality: true, approval: true))));
        return (template, cut, assembly);
    }
}