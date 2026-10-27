using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.HR.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

public partial class HrTests
{
    // Tira Guids e instantes antes de procurar um valor no JSON: "3300" casaria com um pedaço de Guid.
    private static string NoIds(string json) => System.Text.RegularExpressions.Regex.Replace(json, @"[0-9a-f]{8}-[0-9a-f-]{27}|\d{4}-\d{2}-\d{2}T[0-9:.+Z-]+", "#");

    [Fact]
    public async Task Cargo_com_faixa_calcula_27_reais_por_hora_e_o_salario_nao_sai_na_lista()
    {
        var hr = await ActorAsync("hr");
        var schedule = await SeedScheduleAsync(hr);
        var id = await CreatedAsync(await hr.PostAsJsonAsync("/api/v1/hr/positions", Position(Unique("Montador"), schedule)));
        (await JsonAsync(await hr.GetAsync($"/api/v1/hr/positions/{id}"))).GetProperty("monthlyHours").GetDecimal().Should().Be(220m, "44 h × 5 (CA 4)");

        var salary = await hr.PutAsJsonAsync($"/api/v1/hr/positions/{id}/salary", new { salaryMin = 3000m, salaryMax = 3600m, referenceSalary = (decimal?)null });
        salary.StatusCode.Should().Be(HttpStatusCode.OK, await salary.Content.ReadAsStringAsync());
        (await JsonAsync(salary)).GetProperty("hourlyCost").GetDecimal().Should().Be(27m, "3.300 × 1,80 ÷ 220 (CA 3)");

        var raw = await hr.GetStringAsync($"/api/v1/hr/positions/{id}") + await hr.GetStringAsync("/api/v1/hr/positions?sort=salaryMin");
        NoIds(raw).Should().NotContainEquivalentOf("salary").And.NotContain("3300").And.NotContain("3600", "a lista e o detalhe não trazem salário (CA 10 e 12)");

        var sensitive = await hr.GetAsync($"/api/v1/hr/positions/{id}/sensitive");
        sensitive.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await JsonAsync(sensitive)).GetProperty("referenceSalary").GetDecimal().Should().Be(3300m, "média da faixa (CA 2)");
        await using var scope = await ScopeAsync();
        (await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().AuditEntries.AnyAsync(a => a.Entity == "JobPosition" && a.EntityId == id.ToString() && a.Action == "SensitiveRead"))
            .Should().BeTrue("leitura sensível na auditoria (ADR 0016)");
        (await scope.ServiceProvider.GetRequiredService<IHourlyCost>().ForPositionAsync(id)).Should().Be(27m, "contrato só por cargo (CA 8)");

        var manual = await JsonAsync(await hr.PutAsJsonAsync($"/api/v1/hr/positions/{id}/hourly-cost", new { value = 30.5m }));
        manual.GetProperty("hourlyCostManual").GetBoolean().Should().BeTrue();
        manual.GetProperty("hourlyCost").GetDecimal().Should().Be(30.5m, "ajuste manual (CA 5)");
        (await JsonAsync(await hr.DeleteAsync($"/api/v1/hr/positions/{id}/hourly-cost"))).GetProperty("hourlyCost").GetDecimal().Should().Be(27m, "voltar ao calculado");
    }

    [Fact]
    public async Task Sem_view_sensitive_o_salario_e_recusado_e_o_put_geral_com_salario_da_400()
    {
        var hr = await ActorAsync("hr");
        var editor = await ActorAsync("hr:view,edit");
        var schedule = await SeedScheduleAsync(hr);
        var name = Unique("Soldador");
        var id = await CreatedAsync(await hr.PostAsJsonAsync("/api/v1/hr/positions", Position(name, schedule)));
        (await hr.PutAsJsonAsync($"/api/v1/hr/positions/{id}/salary", new { salaryMin = 2500m, salaryMax = 2500m, referenceSalary = 2500m })).StatusCode.Should().Be(HttpStatusCode.OK);

        var denied = await editor.PutAsJsonAsync($"/api/v1/hr/positions/{id}/salary", new { salaryMin = 1m, salaryMax = 1m, referenceSalary = 1m });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(denied)).GetProperty("code").GetString().Should().Be("forbidden");
        (await editor.GetAsync($"/api/v1/hr/positions/{id}/sensitive")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await editor.PutAsJsonAsync($"/api/v1/hr/positions/{id}", new { name, cbo = (string?)null, defaultWorkCenterId = (Guid?)null, workScheduleId = schedule, monthlyHours = (decimal?)null, salaryMin = (decimal?)null }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "salário no corpo dos dados gerais (CA 11)");
        (await editor.PutAsJsonAsync($"/api/v1/hr/positions/{id}", Position(name, schedule, 200m))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JsonAsync(await hr.GetAsync($"/api/v1/hr/positions/{id}/sensitive"))).GetProperty("referenceSalary").GetDecimal().Should().Be(2500m, "a faixa não foi anulada");
        (await JsonAsync(await hr.GetAsync($"/api/v1/hr/positions/{id}"))).GetProperty("hourlyCost").GetDecimal().Should().Be(22.5m, "2.500 × 1,80 ÷ 200");

        (await editor.GetAsync($"/api/v1/audit/JobPosition/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "histórico do HR só com hr.view_sensitive (CA 13)");
        var history = await hr.GetStringAsync($"/api/v1/audit/JobPosition/{id}");
        NoIds(history).Should().Contain("***").And.NotContain("2500", "salário mascarado no histórico");
    }
}
