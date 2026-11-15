using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

/// <summary>USR-01: convite com funcionário (o vínculo nasce com o usuário convidado) e o desligamento que desativa o login vinculado.</summary>
public partial class EmployeesTests
{
    /// <summary>Usuário num grupo com ações de vários módulos ("hr:view,edit;identity:view,create").</summary>
    private async Task<(Guid Id, HttpClient Client)> GrantsActorAsync(string grants)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "u" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            var group = new Group { Name = "USR-01 " + Guid.NewGuid().ToString("N")[..6], Description = "Teste" };
            db.Groups.Add(group);
            foreach (var part in grants.Split(';'))
            {
                var module = part[..part.IndexOf(':', StringComparison.Ordinal)];
                db.GroupModules.Add(new GroupModule { GroupId = group.Id, ModuleCode = module, Enabled = true });
                foreach (var action in part[(module.Length + 1)..].Split(','))
                {
                    db.GroupPermissions.Add(new GroupGrant { GroupId = group.Id, ModuleCode = module, Action = action });
                }
            }

            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = group.Id });
            await db.SaveChangesAsync();
        }

        var client = tenancy.Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return (id, client);
    }

    private async Task<User> UserAsync(Guid id)
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IIdentityDb>().Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private static string NewEmail() => "c" + Guid.NewGuid().ToString("N")[..10] + "@teste.com";

    [Fact]
    public async Task Convite_com_funcionario_grava_o_vinculo_reserva_o_funcionario_e_exige_hr_edit()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, noHrEdit) = await GrantsActorAsync("hr:view;identity:view,create");
        var position = await PositionAsync(owner);
        var employee = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf(), name: "Ana Convidada")));

        // CA 1: o campo oferece só funcionários ativos sem usuário; sem hr.edit, nem a lista nem o vínculo (CA 4).
        (await owner.GetStringAsync("/api/v1/hr/employees/invitable")).Should().Contain(employee.ToString());
        (await noHrEdit.GetAsync("/api/v1/hr/employees/invitable")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var denied = await noHrEdit.PostAsJsonAsync("/api/v1/identity/users/invite", new { name = "Ana", email = NewEmail(), employeeId = employee });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "vincular exige hr.edit, como na ficha");
        (await noHrEdit.PostAsJsonAsync("/api/v1/identity/users/invite", new { name = "Contadora", email = NewEmail() })).StatusCode.Should().Be(HttpStatusCode.Created, "o externo continua sem funcionário");

        // CA 2: o vínculo nasce com o usuário convidado e o funcionário fica reservado.
        var invited = await owner.PostAsJsonAsync("/api/v1/identity/users/invite", new { name = "Ana Convidada", email = NewEmail(), employeeId = employee });
        invited.StatusCode.Should().Be(HttpStatusCode.Created, await invited.Content.ReadAsStringAsync());
        var userId = (await JsonAsync(invited)).GetProperty("id").GetGuid();
        (await UserAsync(userId)).EmployeeId.Should().Be(employee);
        var linked = (await JsonAsync(await owner.GetAsync($"/api/v1/hr/employees/{employee}/user"))).GetProperty("user");
        linked.GetProperty("userId").GetGuid().Should().Be(userId);
        linked.GetProperty("status").GetString().Should().Be("Invited");
        (await owner.GetStringAsync("/api/v1/hr/employees/invitable")).Should().NotContain(employee.ToString(), "o funcionário reservado sai da lista");
        var row = (await JsonAsync(await owner.GetAsync("/api/v1/identity/users"))).EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == userId);
        row.GetProperty("employeeId").GetGuid().Should().Be(employee);
        row.GetProperty("employeeName").GetString().Should().Be("Ana Convidada");

        // CA 3: funcionário já vinculado é recusado, sem criar o usuário.
        var other = NewEmail();
        var twice = await owner.PostAsJsonAsync("/api/v1/identity/users/invite", new { name = "Outra", email = other, employeeId = employee });
        twice.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(twice)).GetProperty("code").GetString().Should().Be("employee_already_linked");
        (await owner.GetStringAsync("/api/v1/identity/users")).Should().NotContain(other);
    }

    [Fact]
    public async Task Desligamento_desativa_o_login_vinculado_e_preserva_o_Proprietario()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, hr) = await ActorAsync("hr:view,edit");
        var position = await PositionAsync(owner);

        // CA 9 e 10: desligado pelo RH, o login vinculado é desativado na hora, com auditoria e a mensagem da conclusão.
        var employee = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf(), name: "Bruno Saindo")));
        var (userId, _) = await ActorAsync();
        (await owner.PutAsJsonAsync($"/api/v1/hr/employees/{employee}/user", new { userId })).EnsureSuccessStatusCode();
        var done = await JsonAsync(await hr.PostAsJsonAsync($"/api/v1/hr/employees/{employee}/termination", new { terminationDate = "2026-09-30" }));
        done.GetProperty("employee").GetProperty("status").GetString().Should().Be("Terminated");
        done.GetProperty("login").GetProperty("outcome").GetString().Should().Be("Deactivated");
        var user = await UserAsync(userId);
        user.Status.Should().Be(UserStatus.Inactive);
        done.GetProperty("login").GetProperty("message").GetString().Should().Be($"O login de {(string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name)} foi desativado.");
        await using (var scope = await ScopeAsync())
        {
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            (await db.AuditEntries.AnyAsync(a => a.EntityId == userId.ToString() && a.Action == "DeactivatedByTermination")).Should().BeTrue();
            (await db.RefreshTokens.AnyAsync(t => t.UserId == userId && t.RevokedAt == null)).Should().BeFalse("as sessões caem como na desativação manual");
        }

        // CA 11: o login de um Proprietário não é desativado por quem não é do Proprietário; o desligamento vale e a conclusão avisa.
        var boss = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        var (ownerUser, _) = await ActorAsync(Group.OwnerSeedKey);
        (await owner.PutAsJsonAsync($"/api/v1/hr/employees/{boss}/user", new { userId = ownerUser })).EnsureSuccessStatusCode();
        var kept = await JsonAsync(await hr.PostAsJsonAsync($"/api/v1/hr/employees/{boss}/termination", new { terminationDate = "2026-09-30" }));
        kept.GetProperty("employee").GetProperty("status").GetString().Should().Be("Terminated");
        kept.GetProperty("login").GetProperty("outcome").GetString().Should().Be("KeptOwner");
        (await UserAsync(ownerUser)).Status.Should().Be(UserStatus.Active);

        // Sem usuário, nada a desativar.
        var alone = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        (await JsonAsync(await hr.PostAsJsonAsync($"/api/v1/hr/employees/{alone}/termination", new { terminationDate = "2026-09-30" })))
            .GetProperty("login").GetProperty("outcome").GetString().Should().Be("NoUser");
    }
}
