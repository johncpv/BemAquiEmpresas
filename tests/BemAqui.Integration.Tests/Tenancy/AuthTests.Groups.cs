using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-06b: grupos iniciais, policy perm:, matriz, delegação, Proprietário protegido e efeito no próximo request.</summary>
public partial class AuthTests
{
    private sealed record Actor(Guid Id, HttpClient Client);

    /// <summary>Usuário do tenant A num grupo do seed (pela seed_key) ou sem grupo, já com token válido.</summary>
    private async Task<Actor> ActorAsync(string? seedKey = null, Guid? groupId = null)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, NewEmail());
        if (seedKey is not null || groupId is not null)
        {
            await using var scope = await TenantAScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = groupId ?? await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group });
            await db.SaveChangesAsync();
        }

        return new Actor(id, Authorized(TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp)));
    }

    private static object Module(string code, params string[] actions) => new { moduleCode = code, enabled = actions.Length > 0, actions };

    private static async Task<Guid> CreatedIdAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> GroupIdAsync(string seedKey)
    {
        await using var scope = await TenantAScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IIdentityDb>().Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync();
    }

    [Fact]
    public async Task Tenant_nasce_com_os_8_grupos_e_o_padrao_da_ADR_0010()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);

        var groups = (await JsonAsync(await owner.Client.GetAsync("/api/v1/identity/groups"))).EnumerateArray().ToList();

        groups.Select(g => g.GetProperty("name").GetString()).Should().Contain(
            ["Proprietário", "Financeiro", "Comercial", "Engenharia", "Supervisão de produção", "Operadores", "Almoxarifado", "RH"]);
        var proprietario = groups.Single(g => g.GetProperty("isSystem").GetBoolean()); // outros testes da classe criam grupos
        proprietario.GetProperty("requiresTwoFactor").GetBoolean().Should().BeTrue();

        static List<string> Actions(JsonElement detail, string module) =>
            detail.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("code").GetString() == module)
                .GetProperty("actions").EnumerateArray().Select(a => a.GetString()!).ToList();
        var finance = await JsonAsync(await owner.Client.GetAsync("/api/v1/identity/groups/" + await GroupIdAsync("finance")));
        Actions(finance, "finance").Should().BeEquivalentTo(["view", "create", "edit"], "o Financeiro não aprova por padrão");
        Actions(await JsonAsync(await owner.Client.GetAsync("/api/v1/identity/groups/" + await GroupIdAsync("hr"))), "hr").Should().Contain("view_sensitive");
        var approve = finance.GetProperty("modules").EnumerateArray().Single(m => m.GetProperty("code").GetString() == "finance")
            .GetProperty("available").EnumerateArray().Single(a => a.GetProperty("code").GetString() == "approve");
        approve.GetProperty("description").GetString().Should().Contain("ordem de pagamento");
    }

    [Fact]
    public async Task Policy_perm_recusa_quem_nao_tem_a_acao_com_forbidden()
    {
        var nobody = await ActorAsync();
        var denied = await nobody.Client.GetAsync("/api/v1/identity/groups");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await JsonAsync(denied);
        body.GetProperty("code").GetString().Should().Be("forbidden");
        body.GetProperty("detail").GetString().Should().Be("Você não tem acesso a esta funcionalidade. Fale com o administrador.");
        (await JsonAsync(await nobody.Client.GetAsync("/api/v1/me/permissions"))).GetProperty("permissions").GetArrayLength().Should().Be(0);

        var owner = await ActorAsync(Group.OwnerSeedKey);
        (await owner.Client.GetAsync("/api/v1/identity/groups")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(await owner.Client.GetAsync("/api/v1/me/permissions"))).GetProperty("isOwner").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Duplicar_cria_copia_sem_membros_e_Proprietario_e_protegido()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var copy = await CreatedIdAsync(await owner.Client.PostAsync($"/api/v1/identity/groups/{await GroupIdAsync("commercial")}/duplicate", null));
        var detail = await JsonAsync(await owner.Client.GetAsync("/api/v1/identity/groups/" + copy));
        detail.GetProperty("group").GetProperty("name").GetString().Should().StartWith("Cópia de Comercial");
        detail.GetProperty("members").GetArrayLength().Should().Be(0);

        var ownerGroup = await GroupIdAsync(Group.OwnerSeedKey);
        var edit = await owner.Client.PutAsJsonAsync("/api/v1/identity/groups/" + ownerGroup, new { name = "X", requiresTwoFactor = false, modules = Array.Empty<object>() });
        edit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(edit)).GetProperty("code").GetString().Should().Be("system_group");
        (await owner.Client.PostAsync($"/api/v1/identity/groups/{ownerGroup}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var empty = await owner.Client.PutAsJsonAsync($"/api/v1/identity/groups/{ownerGroup}/members", new { userIds = Array.Empty<Guid>() });
        empty.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(empty)).GetProperty("detail").GetString().Should().Be("O grupo Proprietário precisa ter ao menos um membro.");
    }
}
