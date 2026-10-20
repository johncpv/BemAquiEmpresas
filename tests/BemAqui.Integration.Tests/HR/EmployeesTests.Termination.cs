using System.Net;
using System.Net.Http.Json;
using BemAqui.HR.Contracts;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

public partial class EmployeesTests
{
    [Fact]
    public async Task Desligamento_mantem_o_registro_e_com_historico_nao_se_exclui()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var position = await PositionAsync(owner);
        var fresh = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        var used = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf(), name: "Carlos Desligado")));
        (await owner.PostAsJsonAsync($"/api/v1/hr/employees/{used}/salary", new { newSalary = 3400m, effectiveDate = "2025-01-01", reason = "Mérito" })).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/v1/hr/positions/{position}")).StatusCode.Should().Be(HttpStatusCode.Conflict, "cargo com funcionário ativo não é excluído");

        (await owner.PostAsJsonAsync($"/api/v1/hr/employees/{used}/termination", new { terminationDate = "2020-01-01" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "antes da admissão");
        var terminated = await JsonAsync(await owner.PostAsJsonAsync($"/api/v1/hr/employees/{used}/termination", new { terminationDate = "2026-09-30" }));
        terminated.GetProperty("status").GetString().Should().Be("Terminated");
        terminated.GetProperty("terminationDate").GetString().Should().Be("2026-09-30");
        (await owner.GetStringAsync("/api/v1/hr/employees?status=Terminated&q=Carlos")).Should().Contain("Carlos Desligado");

        await using (var scope = await ScopeAsync())
        {
            var active = await scope.ServiceProvider.GetRequiredService<IEmployees>().ActiveAsync();
            active.Should().Contain(e => e.Id == fresh).And.NotContain(e => e.Id == used, "o contrato devolve só ativos");
        }

        var delete = await owner.DeleteAsync($"/api/v1/hr/employees/{used}");
        delete.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await delete.Content.ReadAsStringAsync()).Should().Contain("Registre o desligamento.");
        (await owner.DeleteAsync($"/api/v1/hr/employees/{fresh}")).IsSuccessStatusCode.Should().BeTrue("sem histórico, exclusão lógica");
        (await owner.GetAsync($"/api/v1/hr/employees/{fresh}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sem_modulo_no_grupo_nada_de_funcionarios()
    {
        var (_, nobody) = await ActorAsync();
        (await nobody.GetAsync("/api/v1/hr/employees")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (_, viewer) = await ActorAsync("hr:view");
        (await viewer.PostAsJsonAsync("/api/v1/hr/employees/" + Guid.NewGuid() + "/termination", new { terminationDate = "2026-01-01" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "desligar exige hr.edit");
        (await viewer.DeleteAsync("/api/v1/hr/employees/" + Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
