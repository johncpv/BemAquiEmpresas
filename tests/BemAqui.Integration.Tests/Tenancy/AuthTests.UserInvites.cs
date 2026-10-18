using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-06c CA 5 a 11: convite com grupos e delegação, reenviar e cancelar, desativar e reativar.</summary>
public partial class AuthTests
{
    [Fact]
    public async Task Convite_com_grupos_respeita_a_delegacao_e_o_aceite_entra_nos_grupos()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var email = NewEmail();
        var invited = await owner.Client.PostAsJsonAsync("/api/v1/identity/users/invite", new { name = "Bia Lima", email, groupIds = new[] { await GroupIdAsync("commercial") } });
        var userId = await CreatedIdAsync(invited);

        var row = (await JsonAsync(await owner.Client.GetAsync("/api/v1/identity/users"))).EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == userId);
        row.GetProperty("status").GetString().Should().Be("Invited");
        row.GetProperty("inviteOpen").GetBoolean().Should().BeTrue();
        var again = await owner.Client.PostAsJsonAsync("/api/v1/identity/users/invite", new { name = "Bia", email = email.ToUpperInvariant() });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(again)).GetProperty("code").GetString().Should().Be("user_exists");

        var managers = await CreatedIdAsync(await owner.Client.PostAsJsonAsync("/api/v1/identity/groups", new { name = "Convidadores " + Guid.NewGuid().ToString("N")[..6], requiresTwoFactor = false, modules = new[] { Module("identity", "view", "create") } }));
        var manager = await ActorAsync(groupId: managers);
        foreach (var group in new[] { await GroupIdAsync("finance"), await GroupIdAsync(Group.OwnerSeedKey) })
        {
            var denied = await manager.Client.PostAsJsonAsync("/api/v1/identity/users/invite", new { name = "X", email = NewEmail(), groupIds = new[] { group } });
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await JsonAsync(denied)).GetProperty("code").GetString().Should().Be("delegation_denied");
        }

        var token = TokenOf((await LinksSentAsync(email, "invite"))[0]);
        (await PostAsync("/api/v1/auth/invites/accept", new { token, password = TenancyFixture.DefaultPassword })).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scope = await TenantAScopeAsync();
        (await scope.ServiceProvider.GetRequiredService<IIdentityDb>().UserGroups.Where(ug => ug.UserId == userId).Select(ug => ug.GroupId).ToListAsync())
            .Should().Equal(await GroupIdAsync("commercial"));
    }

    [Fact]
    public async Task Reenviar_troca_o_link_e_cancelar_invalida()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var email = NewEmail();
        var userId = await CreatedIdAsync(await owner.Client.PostAsJsonAsync("/api/v1/identity/users/invite", new { name = "Caio", email }));
        var first = TokenOf((await LinksSentAsync(email, "invite"))[0]);

        (await owner.Client.PostAsync($"/api/v1/identity/users/{userId}/resend-invite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var second = TokenOf((await LinksSentAsync(email, "invite"))[0]);
        second.Should().NotBe(first);
        (await InspectAsync(first)).GetProperty("state").GetString().Should().Be("NotFound");
        (await InspectAsync(second)).GetProperty("state").GetString().Should().Be("Valid");

        (await owner.Client.PostAsync($"/api/v1/identity/users/{userId}/cancel-invite", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await InspectAsync(second)).GetProperty("state").GetString().Should().Be("NotFound");
    }

    [Fact]
    public async Task Desativar_derruba_no_proximo_request_reativar_volta_e_o_ultimo_Proprietario_fica()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var email = NewEmail();
        var (id, _) = await tenancy.SeedUserAsync(tenancy.TenantA, email);
        await owner.Client.PutAsJsonAsync($"/api/v1/identity/users/{id}/groups", new { groupIds = new[] { await GroupIdAsync("commercial") } });
        var session = Authorized((await JsonAsync(await LoginAsync(email, TenancyFixture.DefaultPassword))).GetProperty("accessToken").GetString()!);

        (await owner.Client.PostAsync($"/api/v1/identity/users/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await session.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await owner.Client.PostAsync($"/api/v1/identity/users/{id}/reactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoginAsync(email, TenancyFixture.DefaultPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var scope = await TenantAScopeAsync())
        {
            (await scope.ServiceProvider.GetRequiredService<IIdentityDb>().UserGroups.CountAsync(ug => ug.UserId == id)).Should().Be(1, "reativa com os mesmos grupos");
        }

        // Último Proprietário ativo: num tenant novo, só ele no grupo.
        var tenant = await tenancy.ProvisionAsync("empresa-dono", "Empresa Dono Ltda", "dono.localhost");
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(tenant.Id))!;
        var (soleOwner, stamp) = await tenancy.SeedUserAsync(tenant, NewEmail());
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = soleOwner, GroupId = await db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        var client = Authorized(TestTokens.Create(tenant.Id, "dono.localhost", userId: soleOwner, stamp: stamp), "dono.localhost");
        var refused = await client.PostAsync($"/api/v1/identity/users/{soleOwner}/deactivate", null);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(refused)).GetProperty("detail").GetString().Should().Be("O grupo Proprietário precisa ter ao menos um membro ativo.");
    }
}
