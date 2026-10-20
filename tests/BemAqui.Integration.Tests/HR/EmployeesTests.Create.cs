using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Persistence.Auditing;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

public partial class EmployeesTests
{
    [Fact]
    public async Task Criacao_exige_dado_sensivel_valida_cpf_e_avisa_salario_fora_da_faixa()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, editor) = await ActorAsync("hr:view,create,edit");
        var position = await PositionAsync(owner);
        var cpf = NewCpf();

        (await editor.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf()))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "CPF e salário são obrigatórios e sensíveis (CA 2)");

        var ok = await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, cpf));
        var id = await CreatedAsync(ok);
        var body = NoIds(await ok.Content.ReadAsStringAsync());
        body.Should().NotContain(cpf).And.NotContain("3300", "a resposta da criação não devolve dado sensível");
        (await JsonAsync(await owner.GetAsync($"/api/v1/hr/employees/{id}"))).GetProperty("workScheduleId").ValueKind.Should().NotBe(System.Text.Json.JsonValueKind.Null, "a jornada vem do cargo (CA 3)");

        var duplicate = await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, cpf));
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadAsStringAsync()).Should().Contain("employee_cpf_taken");
        (await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, "12345678900"))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var outside = await JsonAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf(), salary: 5000m)));
        outside.GetProperty("warning").GetString().Should().StartWith("Salário fora da faixa do cargo", "o aviso não bloqueia (CA 5)");
    }

    [Fact]
    public async Task Detalhe_e_lista_nunca_trazem_dado_pessoal_e_a_rota_sensivel_registra_a_leitura()
    {
        var (ownerId, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, viewer) = await ActorAsync("hr:view");
        var position = await PositionAsync(owner);
        var cpf = NewCpf();
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, cpf, name: "Joana Sigilo")));
        var pis = NewPis();
        var personal = new { cpf, birthDate = "1990-05-10", rg = "MG-12.345.678", pis, ctpsNumber = "1234567", ctpsSeries = "0012", voterId = (string?)null, zipCode = "30140071", street = "Rua A", number = "10", complement = (string?)null, district = "Centro", city = "Belo Horizonte", state = "MG", phone = "31999990000", email = "joana@exemplo.com" };
        (await owner.PutAsJsonAsync($"/api/v1/hr/employees/{id}/personal", personal)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PutAsJsonAsync($"/api/v1/hr/employees/{id}/personal", personal with { pis = "12345678901" })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "PIS com dígito errado (CA 6)");

        foreach (var raw in new[] { await viewer.GetStringAsync($"/api/v1/hr/employees/{id}"), await viewer.GetStringAsync("/api/v1/hr/employees?q=Joana") })
        {
            NoIds(raw).Should().Contain("Joana Sigilo").And.NotContain(cpf).And.NotContain(pis).And.NotContain("3300").And.NotContain("joana@exemplo", "o JSON bruto não tem dado pessoal (CA 8)");
        }

        (await viewer.GetAsync($"/api/v1/hr/employees/{id}/sensitive")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PutAsJsonAsync($"/api/v1/hr/employees/{id}/personal", personal)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.GetStringAsync($"/api/v1/hr/employees?q={cpf}")).Should().NotContain("Joana", "busca por CPF só com a permissão (CA 11)");
        (await owner.GetStringAsync($"/api/v1/hr/employees?q={cpf}")).Should().Contain("Joana");
        (await owner.GetStringAsync($"/api/v1/hr/employees?q={cpf[..6]}")).Should().NotContain("Joana", "a busca por CPF é exata");

        var sensitive = await owner.GetAsync($"/api/v1/hr/employees/{id}/sensitive");
        sensitive.Headers.CacheControl!.NoStore.Should().BeTrue("CA 9");
        var data = await JsonAsync(sensitive);
        data.GetProperty("cpf").GetString().Should().Be(cpf);
        data.GetProperty("baseSalary").GetDecimal().Should().Be(3300m);
        data.GetProperty("pis").GetString().Should().Be(pis);

        await using var scope = await ScopeAsync();
        var audit = scope.ServiceProvider.GetRequiredService<BemAquiDbContext>();
        (await audit.AuditEntries.CountAsync(a => a.EntityId == id.ToString() && a.Action == AuditActions.SensitiveRead && a.UserId == ownerId)).Should().Be(1, "a leitura sensível fica na auditoria (CA 9)");
        var (_, hrSensitive) = await ActorAsync("hr:view,view_sensitive");
        (await hrSensitive.GetStringAsync($"/api/v1/audit/Employee/{id}")).Should().NotContain("SensitiveRead", "leituras sensíveis só aparecem para o Proprietário (ADR 0016)");
        (await owner.GetStringAsync($"/api/v1/audit/Employee/{id}")).Should().Contain("SensitiveRead");
    }

    [Fact]
    public async Task Dados_gerais_recusam_campo_sensivel_no_corpo()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var (_, editor) = await ActorAsync("hr:view,edit");
        var position = await PositionAsync(owner);
        var id = await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/hr/employees", NewEmployee(position, NewCpf())));
        var smuggled = await editor.PutAsJsonAsync($"/api/v1/hr/employees/{id}", new { name = "Novo nome", registration = "M-01", workScheduleId = (Guid?)null, unionId = (Guid?)null, admissionDate = "2024-03-01", baseSalary = 1m });
        smuggled.StatusCode.Should().Be(HttpStatusCode.BadRequest, "campo sensível no comando de dados gerais (CA 10)");
        (await editor.PutAsJsonAsync($"/api/v1/hr/employees/{id}", new { name = "Novo nome", registration = "M-01", workScheduleId = (Guid?)null, unionId = (Guid?)null, admissionDate = "2024-03-01" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(await owner.GetAsync($"/api/v1/hr/employees/{id}/sensitive"))).GetProperty("baseSalary").GetDecimal().Should().Be(3300m, "o salário gravado não muda");
        (await editor.PostAsJsonAsync($"/api/v1/hr/employees/{id}/salary", new { newSalary = 4000m, effectiveDate = (string?)null, reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
