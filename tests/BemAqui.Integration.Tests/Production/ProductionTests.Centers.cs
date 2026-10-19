using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Production.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

public partial class ProductionTests
{
    [Fact]
    public async Task Centro_com_dois_turnos_grava_8_80_no_calendario_padrao_e_auditado()
    {
        var supervisor = await ActorAsync("production_supervision");
        var calendarId = (await JsonAsync(await supervisor.GetAsync("/api/v1/production/calendars/default"))).GetProperty("id").GetGuid();

        var id = await CreatedAsync(await supervisor.PostAsJsonAsync("/api/v1/production/work-centers", Center(Unique("Corte"), ("12:00", "16:48"), ("7:00", "11:00"))));

        var center = await JsonAsync(await supervisor.GetAsync($"/api/v1/production/work-centers/{id}"));
        center.GetProperty("hoursPerDay").GetDecimal().Should().Be(8.80m);
        center.GetProperty("calendarId").GetGuid().Should().Be(calendarId, "o centro usa o calendário padrão (CA 12)");
        center.GetProperty("shifts")[0].GetProperty("start").GetString().Should().Be("07:00", "os turnos ficam em ordem e com dois dígitos");
        (await QueryAsync(db => db.AuditEntries.AnyAsync(a => a.Entity == "WorkCenter" && a.EntityId == id.ToString()))).Should().BeTrue("auditado (CA 20)");

        var suggested = await supervisor.GetFromJsonAsync<Shift[]>("/api/v1/production/work-centers/suggested-shifts");
        suggested.Should().NotBeEmpty("o próximo centro vem com os turnos do último (propositivo)");
    }

    [Fact]
    public async Task Turnos_sobrepostos_que_viram_o_dia_ou_nome_repetido_sao_recusados()
    {
        var supervisor = await ActorAsync("production_supervision");
        var overlap = await supervisor.PostAsJsonAsync("/api/v1/production/work-centers", Center(Unique("Solda"), ("07:00", "11:00"), ("10:00", "12:00")));
        overlap.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await overlap.Content.ReadAsStringAsync()).Should().Contain("Os turnos não podem se sobrepor.");
        var night = await supervisor.PostAsJsonAsync("/api/v1/production/work-centers", Center(Unique("Solda"), ("22:00", "06:00")));
        (await night.Content.ReadAsStringAsync()).Should().Contain("Turno que vira o dia não é aceito.");

        var name = Unique("Pintura");
        await CreatedAsync(await supervisor.PostAsJsonAsync("/api/v1/production/work-centers", Center(name, ("07:00", "16:00"))));
        var twice = await supervisor.PostAsJsonAsync("/api/v1/production/work-centers", Center(name.ToUpperInvariant(), ("07:00", "16:00")));
        twice.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(twice)).Should().Be("work_center_name_taken");
    }

    [Fact]
    public async Task Inativo_some_dos_seletores_zera_a_capacidade_e_em_uso_nao_exclui()
    {
        var owner = await ActorAsync(BemAqui.Identity.Domain.Group.OwnerSeedKey);
        var supervisor = await ActorAsync("production_supervision");
        var used = await CreatedAsync(await supervisor.PostAsJsonAsync("/api/v1/production/work-centers", Center(Unique("Dobra"), ("07:00", "15:00"))));
        var free = await CreatedAsync(await supervisor.PostAsJsonAsync("/api/v1/production/work-centers", Center(Unique("Corte a laser"), ("07:00", "15:00"))));
        InUse[used] = true;

        (await supervisor.DeleteAsync($"/api/v1/production/work-centers/{free}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "excluir pede production.delete, que só o Proprietário tem (CA 1)");
        var refused = await owner.DeleteAsync($"/api/v1/production/work-centers/{used}");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("work_center_in_use");
        problem.GetProperty("detail").GetString().Should().Be("O centro está em uso. Inative-o.");
        (await owner.DeleteAsync($"/api/v1/production/work-centers/{free}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/v1/production/work-centers/{free}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await supervisor.PostAsync($"/api/v1/production/work-centers/{used}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var scope = await ScopeAsync();
        (await scope.ServiceProvider.GetRequiredService<IWorkCenters>().ActiveAsync()).Should().NotContain(c => c.Id == used, "inativo some dos seletores (CA 18)");
        (await scope.ServiceProvider.GetRequiredService<IWorkCenters>().FindAsync([used])).Should().ContainSingle(c => !c.IsActive, "e continua resolvido por id");
        var capacity = await scope.ServiceProvider.GetRequiredService<IWorkCenterCapacity>().GetAvailableHoursAsync(used, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20));
        capacity!.TotalHours.Should().Be(0m, "centro inativo tem capacidade 0 (CA 14 e 16)");
    }

    [Fact]
    public async Task Capacidade_da_fixture_planejamento_base_da_8_8_4_8_0_0_0()
    {
        await using var scope = await ScopeAsync();
        var ids = await PlanningBase.SeedAsync(scope.ServiceProvider);
        var capacity = scope.ServiceProvider.GetRequiredService<IWorkCenterCapacity>();

        var cut = await capacity.GetAvailableHoursAsync(ids.Cut, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 22));
        cut!.Days.Select(d => d.Hours).Should().Equal(8m, 8m, 4m, 8m, 0m, 0m, 0m);
        cut.TotalHours.Should().Be(28m);
        var assembly = await capacity.GetAvailableHoursAsync(ids.Assembly, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20));
        assembly!.TotalHours.Should().Be((8.80m * 3) + 4m);
        (await capacity.GetAvailableHoursAsync(ids.Cut, new DateOnly(2025, 12, 29), new DateOnly(2025, 12, 31)))!.TotalHours.Should().Be(0m, "antes da criação do centro (CA 16)");
        (await capacity.GetAvailableHoursAsync(Guid.CreateVersion7(), new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16))).Should().BeNull();
    }

    [Fact]
    public async Task Tres_camadas_em_todo_endpoint_de_producao()
    {
        var nobody = await ActorAsync();
        var noGroup = await nobody.GetAsync("/api/v1/production/work-centers");
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(noGroup)).Should().Be("forbidden");
        var viewer = await ActorAsync("view:production");
        (await viewer.GetAsync("/api/v1/production/work-centers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync("/api/v1/production/work-centers", Center(Unique("Sem permissão"), ("07:00", "11:00")))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/production", StringComparison.Ordinal) == true).ToList();
        endpoints.Should().HaveCountGreaterThan(14);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("production", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix + "production.", StringComparison.Ordinal), endpoint.DisplayName);
        }
    }
}
