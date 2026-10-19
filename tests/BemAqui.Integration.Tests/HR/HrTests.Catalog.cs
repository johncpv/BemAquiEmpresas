using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.HR;

public partial class HrTests
{
    [Fact]
    public async Task Jornadas_e_sindicatos()
    {
        var hr = await ActorAsync("hr");
        (await JsonAsync(await hr.GetAsync("/api/v1/hr/schedules"))).EnumerateArray().Should().Contain(s => s.GetProperty("name").GetString() == "44 h — seg a sex 08:00–17:48" && s.GetProperty("weeklyHours").GetDecimal() == 44m, "seed (CA 16)");
        var days = Enumerable.Range(1, 6).Select(d => new { weekday = d, start = "07:00", end = d == 6 ? "11:00" : "16:00", breakMinutes = d == 6 ? 0 : 60 }).ToArray();
        var schedule = await JsonAsync(await hr.PostAsJsonAsync("/api/v1/hr/schedules", new { name = Unique("44 h seg a sáb"), days }));
        schedule.GetProperty("weeklyHours").GetDecimal().Should().Be(44m, "5 × 8 h + 4 h no sábado (CA 15)");
        schedule.GetProperty("monthlyHours").GetDecimal().Should().Be(220m);
        var wrong = await hr.PostAsJsonAsync("/api/v1/hr/schedules", new { name = Unique("Errada"), days = new[] { new { weekday = 1, start = "17:00", end = "08:00", breakMinutes = 0 } } });
        wrong.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await wrong.Content.ReadAsStringAsync()).Should().Contain("A saída precisa ser depois da entrada.");

        (await hr.PostAsJsonAsync("/api/v1/hr/unions", new { name = Unique("Sindicato"), document = "11.111.111/1111-11", baseMonth = 5 })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var union = await JsonAsync(await hr.PostAsJsonAsync("/api/v1/hr/unions", new { name = Unique("Sindicato dos Metalúrgicos"), document = "60.701.190/0001-04", baseMonth = 5 }));
        union.GetProperty("document").GetString().Should().Be("60701190000104");
    }

    [Fact]
    public async Task Tres_camadas_em_todo_endpoint_do_RH()
    {
        var nobody = await ActorAsync();
        (await nobody.GetAsync("/api/v1/hr/positions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var viewer = await ActorAsync("hr:view");
        (await viewer.GetAsync("/api/v1/hr/positions")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync("/api/v1/hr/positions", Position(Unique("Sem permissão"), null, 220m))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/hr", StringComparison.Ordinal) == true).ToList();
        endpoints.Should().HaveCountGreaterThan(20);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("hr", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix + "hr.", StringComparison.Ordinal), endpoint.DisplayName);
        }
    }
}
