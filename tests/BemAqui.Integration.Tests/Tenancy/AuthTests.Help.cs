using System.Net;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-14 CA 2, 3 e 6: índice da ajuda pela permissão efetiva, 403 com o código da camada e "já vista" no perfil.</summary>
public partial class AuthTests
{
    private static async Task<List<string>> HelpKeysAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/v1/ajuda"))).EnumerateArray().Select(d => d.GetProperty("key").GetString()!).ToList();

    [Fact]
    public async Task Indice_da_ajuda_segue_a_permissao_e_o_modulo_liberado()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var nobody = await ActorAsync();

        var ownerKeys = await HelpKeysAsync(owner.Client);
        ownerKeys.Should().Contain(["inicio", "acessos-grupos", "configuracoes-seguranca"]).And.NotContain(k => k.StartsWith("plataforma", StringComparison.Ordinal));
        (await HelpKeysAsync(nobody.Client)).Should().Contain("inicio").And.NotContain("acessos-grupos");

        var forbidden = await nobody.Client.GetAsync("/api/v1/ajuda/acessos-grupos");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(forbidden)).GetProperty("code").GetString().Should().Be("forbidden");
        (await owner.Client.GetAsync("/api/v1/ajuda/plataforma-tenants")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "settings").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, false));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        try
        {
            var notReleased = await owner.Client.GetAsync("/api/v1/ajuda/configuracoes-seguranca");
            (await JsonAsync(notReleased)).GetProperty("code").GetString().Should().Be("module_not_released");
            (await HelpKeysAsync(owner.Client)).Should().NotContain("configuracoes-seguranca");
        }
        finally
        {
            await using var scope = tenancy.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "settings").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, true));
            tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
        }
    }

    [Fact]
    public async Task Ajuda_vista_fica_no_perfil_e_vale_em_outra_sessao()
    {
        var user = await ActorAsync();
        (await JsonAsync(await user.Client.GetAsync("/api/v1/ajuda/vistas"))).GetArrayLength().Should().Be(0);

        (await user.Client.PostAsync("/api/v1/ajuda/inicio/vista", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await user.Client.PostAsync("/api/v1/ajuda/inicio/vista", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await JsonAsync(await user.Client.GetAsync("/api/v1/ajuda/vistas"))).EnumerateArray().Select(k => k.GetString()).Should().Equal("inicio");
    }
}
