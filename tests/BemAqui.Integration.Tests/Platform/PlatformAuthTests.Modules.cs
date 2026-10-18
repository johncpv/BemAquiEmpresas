using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Modules;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Platform.Application.Modules;
using BemAqui.Platform.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Platform;

/// <summary>FND-06: catálogo no banco do tenant, herança do plano com exceção e liberação sem deploy.</summary>
public partial class PlatformAuthTests
{
    private async Task<Dictionary<string, bool>> TenantModulesSeenByUserAsync()
    {
        var client = tenancy.ClientFor("a.localhost");
        client.DefaultRequestHeaders.Authorization = new("Bearer", await tenancy.TokenAsync(tenancy.TenantA, "a.localhost"));
        var body = await client.GetFromJsonAsync<JsonElement>("/api/v1/modules");
        return body.EnumerateArray().ToDictionary(m => m.GetProperty("code").GetString()!, m => m.GetProperty("released").GetBoolean());
    }

    [Fact]
    public async Task Seed_do_catalogo_no_tenant_e_idempotente()
    {
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenancy.TenantA.Id))!;
        for (var run = 0; run < 2; run++)
        {
            await using var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
            foreach (var seeder in scope.ServiceProvider.GetServices<ITenantSeeder>())
            {
                await seeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
            }
        }

        await using var check = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info);
        var db = check.ServiceProvider.GetRequiredService<IIdentityDb>();
        (await db.CatalogModules.Select(m => m.Code).ToListAsync()).Should().BeEquivalentTo(ModuleCatalog.All.Select(m => m.Code));
        (await db.CatalogModuleActions.CountAsync()).Should().Be(ModuleCatalog.AllPermissions.Count());
        (await db.CatalogModuleActions.SingleAsync(a => a.ModuleCode == "hr" && a.Action == "view_sensitive")).IsStandard.Should().BeFalse();
        (await db.CatalogModules.SingleAsync(m => m.Code == "finance")).ApproveMeaning.Should().Be("ordem de pagamento, dentro da alçada");
    }

    [Fact]
    public async Task Bloquear_e_liberar_vale_no_proximo_request_e_fica_auditado()
    {
        (await TenantModulesSeenByUserAsync())["finance"].Should().BeTrue("o tenant herdou o plano completo");
        var admin = await AdminWithSessionAsync();
        var platform = WithCookie(Client(), admin.Cookie);
        var path = $"/api/v1/platform/tenants/{tenancy.TenantA.Id}/modules";

        (await platform.PutAsJsonAsync(path, new { modules = new[] { new { code = "finance", released = false } } })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await TenantModulesSeenByUserAsync())["finance"].Should().BeFalse();
        var row = (await platform.GetFromJsonAsync<JsonElement>(path)).EnumerateArray().Single(m => m.GetProperty("code").GetString() == "finance");
        row.GetProperty("released").GetBoolean().Should().BeFalse();
        row.GetProperty("isException").GetBoolean().Should().BeTrue();
        (await platform.PutAsJsonAsync(path, new { modules = new[] { new { code = "crm", released = true } } })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await platform.PutAsJsonAsync(path, new { modules = new[] { new { code = "finance", released = true } } })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TenantModulesSeenByUserAsync())["finance"].Should().BeTrue();
        var actions = await CatalogAsync(db => db.AuditEntries.Where(a => a.Entity == "TenantModule" && a.EntityId == tenancy.TenantA.Id + ":finance").Select(a => a.Action).ToListAsync());
        actions.Should().ContainInOrder("ModuleBlocked", "ModuleReleased");
        (await Client("a.localhost").GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.NotFound, "a API da plataforma não existe no host do tenant");
    }

    [Fact]
    public async Task Troca_de_plano_segue_o_plano_novo_e_preserva_as_excecoes()
    {
        var tenant = await tenancy.ProvisionAsync("empresa-plano", "Empresa Plano Ltda", "plano.localhost");
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ITenantModulesDb>();
        var service = scope.ServiceProvider.GetRequiredService<TenantModuleService>();
        (await db.TenantModules.CountAsync(m => m.TenantId == tenant.Id && m.Enabled)).Should().Be(ModuleCatalog.All.Count);

        (await service.SaveAsync(tenant.Id, [new ModuleReleaseChange("hr", false)], null, null)).IsSuccess.Should().BeTrue();
        var basic = new Plan { Name = "Básico " + Guid.NewGuid().ToString("N")[..6] };
        db.Plans.Add(basic);
        db.PlanModules.AddRange(new[] { "identity", "settings", "hr" }.Select(c => new PlanModule { PlanId = basic.Id, ModuleCode = c }));
        await db.SaveChangesAsync();

        (await service.ChangePlanAsync(tenant.Id, basic.Id, null)).IsSuccess.Should().BeTrue();

        var released = (await service.GetAsync(tenant.Id)).Value.Where(m => m.Released).Select(m => m.Code);
        released.Should().BeEquivalentTo(["identity", "settings"], "o hr bloqueado à mão continua bloqueado; o resto segue o plano novo");
    }
}
