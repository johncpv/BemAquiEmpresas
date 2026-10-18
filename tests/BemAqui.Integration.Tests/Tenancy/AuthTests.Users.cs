using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-06c CA 1 a 4, 12 e 13: as três camadas, união de grupos, revogação, 2FA de quem aprova e Segurança.</summary>
public partial class AuthTests
{
    private static object Settings(bool require) => new { requireTwoFactor = require };

    [Fact]
    public async Task Tres_camadas_respondem_module_not_released_forbidden_forbidden_e_200()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var viewers = await CreatedIdAsync(await owner.Client.PostAsJsonAsync("/api/v1/identity/groups", new { name = "Leitura de config " + Guid.NewGuid().ToString("N")[..6], requiresTwoFactor = false, modules = new[] { Module("settings", "view") } }));
        var viewer = await ActorAsync(groupId: viewers);
        var nobody = await ActorAsync();

        async Task<string?> CodeAsync(HttpResponseMessage r) => r.StatusCode == HttpStatusCode.OK ? "200" : (await JsonAsync(r)).GetProperty("code").GetString();

        (await CodeAsync(await nobody.Client.PutAsJsonAsync("/api/v1/settings/security", Settings(false)))).Should().Be("forbidden", "(b) liberado, sem grupo que habilite");
        (await CodeAsync(await viewer.Client.PutAsJsonAsync("/api/v1/settings/security", Settings(false)))).Should().Be("forbidden", "(c) habilitado, sem a ação");
        (await CodeAsync(await owner.Client.PutAsJsonAsync("/api/v1/settings/security", Settings(false)))).Should().Be("200", "(d) liberado, habilitado e com a ação");
        (await viewer.Client.GetAsync("/api/v1/settings/security")).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "settings")
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, false));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        try
        {
            (await CodeAsync(await owner.Client.PutAsJsonAsync("/api/v1/settings/security", Settings(false)))).Should().Be("module_not_released", "(a) não liberado, até para o Proprietário");
        }
        finally
        {
            await using var scope = tenancy.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "settings")
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, true));
            tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        }
    }

    [Fact]
    public async Task Uniao_dos_grupos_e_remocao_vale_no_proximo_request_com_versao_nova()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var user = await ActorAsync("finance");
        (await owner.Client.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/groups", new { groupIds = new[] { await GroupIdAsync("finance"), await GroupIdAsync("commercial") } }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var first = await user.Client.GetAsync("/api/v1/me/permissions");
        var body = await JsonAsync(first);
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Contain(["finance.edit", "quotes.create", "projects.view"]);
        var finance = body.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("code").GetString() == "finance");
        finance.GetProperty("released").GetBoolean().Should().BeTrue();
        finance.GetProperty("enabled").GetBoolean().Should().BeTrue();
        finance.GetProperty("area").GetString().Should().Be("Financeiro");
        var version = first.Headers.GetValues("X-Perm-Version").Single();

        (await owner.Client.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/groups", new { groupIds = Array.Empty<Guid>() })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await user.Client.GetAsync("/api/v1/me/permissions");
        (await JsonAsync(after)).GetProperty("permissions").GetArrayLength().Should().Be(0);
        after.Headers.GetValues("X-Perm-Version").Single().Should().NotBe(version, "o front recarrega quando a versão muda (CA 15)");
    }

    [Fact]
    public async Task Quem_aprova_precisa_de_2FA_e_Seguranca_liga_a_exigencia_para_todos()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var approvers = await CreatedIdAsync(await owner.Client.PostAsJsonAsync("/api/v1/identity/groups", new { name = "Aprovadores " + Guid.NewGuid().ToString("N")[..6], requiresTwoFactor = false, modules = new[] { Module("finance", "approve") } }));
        var email = NewEmail();
        var (id, _) = await tenancy.SeedUserAsync(tenancy.TenantA, email);
        (await owner.Client.PutAsJsonAsync($"/api/v1/identity/groups/{approvers}/members", new { userIds = new[] { id } })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(await LoginAsync(email, TenancyFixture.DefaultPassword))).GetProperty("twoFactor").GetProperty("purpose").GetString().Should().Be("setup");

        var plain = NewEmail();
        await tenancy.SeedUserAsync(tenancy.TenantA, plain);
        (await JsonAsync(await LoginAsync(plain, TenancyFixture.DefaultPassword))).GetProperty("accessToken").GetString().Should().NotBeNull();
        try
        {
            (await JsonAsync(await owner.Client.PutAsJsonAsync("/api/v1/settings/security", Settings(true)))).GetProperty("requireTwoFactor").GetBoolean().Should().BeTrue();
            (await JsonAsync(await LoginAsync(plain, TenancyFixture.DefaultPassword))).GetProperty("twoFactor").GetProperty("purpose").GetString().Should().Be("setup");
        }
        finally
        {
            await owner.Client.PutAsJsonAsync("/api/v1/settings/security", Settings(false));
        }
    }
}
