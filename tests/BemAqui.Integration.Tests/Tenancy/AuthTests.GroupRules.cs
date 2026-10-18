using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-06b CA 3, 4, 9, 12 a 16: matriz no servidor, delegação, revogação no próximo request e auditoria.</summary>
public partial class AuthTests
{
    [Fact]
    public async Task Delegacao_matriz_e_revogacao_valem_no_proximo_request()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var managers = await CreatedIdAsync(await owner.Client.PostAsJsonAsync("/api/v1/identity/groups", new
        {
            name = "Gestores " + Guid.NewGuid().ToString("N")[..6],
            requiresTwoFactor = false,
            modules = new[] { Module("identity", "view", "create", "edit"), Module("finance", "view", "edit") },
        }));
        var manager = await ActorAsync(groupId: managers);

        var created = await CreatedIdAsync(await manager.Client.PostAsJsonAsync("/api/v1/identity/groups", new { name = "Conferência " + Guid.NewGuid().ToString("N")[..6], requiresTwoFactor = false, modules = new[] { Module("finance", "edit") } }));
        var detail = await JsonAsync(await manager.Client.GetAsync("/api/v1/identity/groups/" + created));
        var finance = detail.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("code").GetString() == "finance");
        finance.GetProperty("enabled").GetBoolean().Should().BeTrue();
        finance.GetProperty("actions").EnumerateArray().Select(a => a.GetString()).Should().BeEquivalentTo(["edit", "view"], "marcar outra ação marca Ver (CA 3)");

        async Task DeniedAsync(HttpResponseMessage response)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
            (await JsonAsync(response)).GetProperty("code").GetString().Should().Be("delegation_denied");
        }

        await DeniedAsync(await manager.Client.PutAsJsonAsync("/api/v1/identity/groups/" + created, new { name = "Conferência", requiresTwoFactor = false, modules = new[] { Module("finance", "view", "approve") } }));
        await DeniedAsync(await manager.Client.PutAsJsonAsync("/api/v1/identity/groups/" + created, new { name = "Conferência", requiresTwoFactor = false, modules = new[] { Module("inventory", "view") } }));
        await DeniedAsync(await manager.Client.PutAsJsonAsync($"/api/v1/identity/groups/{await GroupIdAsync(Group.OwnerSeedKey)}/members", new { userIds = new[] { manager.Id, owner.Id } }));

        await DeniedAsync(await manager.Client.PutAsJsonAsync("/api/v1/identity/groups/" + managers, new { name = "Gestores", requiresTwoFactor = false, modules = new[] { Module("identity", "view") } }));
        await DeniedAsync(await manager.Client.PutAsJsonAsync("/api/v1/identity/groups/" + created, new { name = "Conferência", requiresTwoFactor = false, modules = new[] { Module("identity", "view", "edit") } }));

        var member = await ActorAsync(groupId: managers);
        (await member.Client.GetAsync("/api/v1/identity/groups")).StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await JsonAsync(await owner.Client.PutAsJsonAsync("/api/v1/identity/groups/" + managers, new { name = "Gestores revogados " + Guid.NewGuid().ToString("N")[..6], requiresTwoFactor = false, modules = new[] { Module("finance", "view") } }));
        saved.GetProperty("affectedUsers").GetInt32().Should().Be(2);
        (await member.Client.GetAsync("/api/v1/identity/groups")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "revogação vale no próximo request (CA 15)");

        var deactivated = await JsonAsync(await owner.Client.PostAsync($"/api/v1/identity/groups/{managers}/deactivate", null));
        deactivated.GetProperty("affectedUsers").GetInt32().Should().Be(2);
        (await JsonAsync(await member.Client.GetAsync("/api/v1/me/permissions"))).GetProperty("permissions").GetArrayLength().Should().Be(0);

        await using var scope = await TenantAScopeAsync();
        var actions = await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries
            .Where(a => a.Entity == "Group" && a.EntityId == managers.ToString()).Select(a => a.Action).ToListAsync();
        actions.Should().Contain(["GroupCreated", "PermissionsChanged", "GroupDeactivated"]);
        (await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().Notifications.CountAsync(n => n.UserId == owner.Id && n.Type == "group_changed"))
            .Should().BeGreaterThan(0, "mudanças feitas por outros são notificadas ao Proprietário (ADR 0008)");
    }

    [Fact]
    public async Task Modulo_nao_liberado_responde_module_not_released_e_nao_aceita_permissao()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        async Task ReleaseAsync(string module, bool enabled)
        {
            await using var scope = tenancy.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == module)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, enabled));
            tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        }

        try
        {
            await ReleaseAsync("hr", false);
            var hr = await owner.Client.PostAsJsonAsync("/api/v1/identity/groups", new { name = "RH 2 " + Guid.NewGuid().ToString("N")[..6], requiresTwoFactor = false, modules = new[] { Module("hr", "view") } });
            hr.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await JsonAsync(hr)).GetProperty("code").GetString().Should().Be("module_not_released");
            (await JsonAsync(await owner.Client.GetAsync("/api/v1/me/permissions"))).GetProperty("permissions").EnumerateArray()
                .Select(p => p.GetString()).Should().NotContain(p => p!.StartsWith("hr.", StringComparison.Ordinal), "nem o Proprietário tem módulo não liberado");

            await ReleaseAsync("identity", false);
            var blocked = await owner.Client.GetAsync("/api/v1/identity/groups");
            blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await JsonAsync(blocked)).GetProperty("code").GetString().Should().Be("module_not_released");
        }
        finally
        {
            await ReleaseAsync("hr", true);
            await ReleaseAsync("identity", true);
        }
    }

    [Fact]
    public async Task Grupo_que_exige_2FA_leva_o_membro_a_configuracao_no_login()
    {
        var email = NewEmail();
        var (id, _) = await tenancy.SeedUserAsync(tenancy.TenantA, email);
        await using (var scope = await TenantAScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Identity.Application.IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        var login = await JsonAsync(await LoginAsync(email, TenancyFixture.DefaultPassword));

        login.GetProperty("twoFactor").GetProperty("purpose").GetString().Should().Be("setup");
    }
}
