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
    public async Task Reajuste_e_troca_de_cargo_entram_na_linha_do_tempo_e_o_salario_so_aparece_com_permissao()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("hr:view,edit");
        var position = await PositionAsync(owner, "Soldador");
        var other = await PositionAsync(owner, "Líder");
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));

        (await owner.PostAsJsonAsync($"/api/v1/hr/employees/{id}/salary", new { newSalary = 3500m, effectiveDate = (string?)null, reason = "Dissídio" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "vigência obrigatória");
        (await owner.PostAsJsonAsync($"/api/v1/hr/employees/{id}/salary", new { newSalary = 3500m, effectiveDate = "2025-05-01", reason = "Dissídio" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(await owner.GetAsync($"/api/v1/hr/employees/{id}/sensitive"))).GetProperty("baseSalary").GetDecimal().Should().Be(3500m);

        (await viewer.PostAsJsonAsync($"/api/v1/hr/employees/{id}/position", new { positionId = other, effectiveDate = "2025-08-01", reason = "Promoção", newSalary = 4000m })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "salário junto com a troca exige a permissão sensível");
        (await viewer.PostAsJsonAsync($"/api/v1/hr/employees/{id}/position", new { positionId = position, effectiveDate = "2025-08-01", reason = "Promoção", newSalary = (decimal?)null })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "cargo igual ao atual");
        (await viewer.PostAsJsonAsync($"/api/v1/hr/employees/{id}/position", new { positionId = other, effectiveDate = "2025-08-01", reason = "Promoção", newSalary = (decimal?)null })).StatusCode.Should().Be(HttpStatusCode.OK, "troca de cargo com hr.edit (CA 15)");
        (await JsonAsync(await viewer.GetAsync($"/api/v1/hr/employees/{id}"))).GetProperty("positionId").GetGuid().Should().Be(other);

        var full = (await JsonAsync(await owner.GetAsync($"/api/v1/hr/employees/{id}/timeline"))).EnumerateArray().ToList();
        full.Select(e => e.GetProperty("kind").GetString()).Should().Equal("Position", "Salary");
        full[1].GetProperty("oldValue").GetString().Should().Be("3300.00");
        full[1].GetProperty("newValue").GetString().Should().Be("3500.00");
        var limited = NoIds(await viewer.GetStringAsync($"/api/v1/hr/employees/{id}/timeline"));
        limited.Should().Contain("Promoção").And.NotContain("3500", "reajuste só com hr.view_sensitive (CA 16)");

        (await viewer.GetAsync($"/api/v1/audit/Employee/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "histórico do HR só com hr.view_sensitive");
        var history = NoIds(await owner.GetStringAsync($"/api/v1/audit/Employee/{id}"));
        history.Should().Contain("***").And.NotContain("3500", "dado pessoal mascarado no histórico (CA 18)");
    }
}
