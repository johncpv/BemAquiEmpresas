using System.Net;
using System.Net.Http.Json;
using BemAqui.Identity.Domain;
using FluentAssertions;

namespace BemAqui.Integration.Tests.HR;

public partial class EmployeesTests
{
    [Fact]
    public async Task Vinculo_com_usuario_exige_identity_edit_e_um_usuario_so_tem_um_funcionario()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, hr) = await ActorAsync("hr:view,edit,view_sensitive");
        var (userId, _) = await ActorAsync();
        var position = await PositionAsync(owner);
        var first = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        var second = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));

        (await hr.PutAsJsonAsync($"/api/v1/hr/employees/{first}/user", new { userId })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "vincular usuário exige identity.edit (CA 19)");
        (await owner.GetStringAsync("/api/v1/hr/employees/linkable-users")).Should().Contain(userId.ToString());
        (await owner.PutAsJsonAsync($"/api/v1/hr/employees/{first}/user", new { userId })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(await hr.GetAsync($"/api/v1/hr/employees/{first}/user"))).GetProperty("user").GetProperty("userId").GetGuid().Should().Be(userId);
        (await owner.GetStringAsync("/api/v1/hr/employees/linkable-users")).Should().NotContain(userId.ToString(), "o já vinculado sai da lista");
        (await owner.PutAsJsonAsync($"/api/v1/hr/employees/{second}/user", new { userId })).StatusCode.Should().Be(HttpStatusCode.Conflict, "um usuário, um funcionário (CA 20)");

        (await hr.DeleteAsync($"/api/v1/hr/employees/{first}/user")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.DeleteAsync($"/api/v1/hr/employees/{first}/user")).IsSuccessStatusCode.Should().BeTrue();
        (await JsonAsync(await owner.GetAsync($"/api/v1/hr/employees/{first}/user"))).GetProperty("user").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        (await owner.PutAsJsonAsync($"/api/v1/hr/employees/{second}/user", new { userId })).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
