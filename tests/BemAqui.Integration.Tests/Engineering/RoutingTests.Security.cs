using System.Net;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Engineering;

public partial class RoutingTests
{
    [Fact]
    public async Task Tres_camadas_em_todo_endpoint_de_engenharia()
    {
        var nobody = await ActorAsync();
        var noGroup = await nobody.GetAsync(Base);
        noGroup.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(noGroup)).Should().Be("forbidden");
        var viewer = await ActorAsync("view:engineering");
        (await viewer.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.PostAsJsonAsync(Base, Template(Unique("Sem permissão")))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var supervisor = await ActorAsync("production_supervision");
        (await supervisor.GetAsync(Base)).StatusCode.Should().Be(HttpStatusCode.OK, "a Supervisão ganha engineering.view pelo seed patch (CA 1)");

        await SetReleasedAsync(false);
        try
        {
            var notReleased = await supervisor.GetAsync(Base);
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetReleasedAsync(true);
        }

        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/engineering", StringComparison.Ordinal) == true).ToList();
        endpoints.Should().HaveCountGreaterThan(15);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("engineering", endpoint.DisplayName);
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix + "engineering.", StringComparison.Ordinal), endpoint.DisplayName);
        }
    }

    private async Task SetReleasedAsync(bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == "engineering").ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        // O cache do tenant é do host inteiro (jobs e outbox também resolvem o tenant): confere que a troca já vale antes de seguir.
        var resolver = tenancy.Factory.Services.GetRequiredService<ITenantResolver>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        do
        {
            resolver.Invalidate();
            if ((await resolver.FindByIdAsync(tenancy.TenantA.Id))?.IsReleased("engineering") == released)
            {
                return;
            }

            await Task.Delay(50);
        }
        while (DateTime.UtcNow < deadline);

        throw new TimeoutException("O módulo engineering não mudou no cache do tenant.");
    }

    [Fact]
    public async Task Etapa_ou_vinculo_de_outro_roteiro_da_404_e_corpo_com_campo_extra_e_recusado()
    {
        var engineer = await EngineerAsync();
        var (a, _, _) = await ThreeStepsAsync(engineer);
        var (b, _, assembly) = await ThreeStepsAsync(engineer);
        var (aId, bId) = (a.GetProperty("id").GetGuid(), b.GetProperty("id").GetGuid());
        var stepOfB = b.GetProperty("steps")[0].GetProperty("id").GetGuid();
        var (item, _) = await ItemAsync();
        var linkOfB = (await CreatedAsync(await engineer.PostAsJsonAsync($"{Base}/{bId}/items", new { itemId = item }))).GetProperty("id").GetGuid();

        (await engineer.GetAsync($"{Base}/{bId}/steps/{stepOfB}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await engineer.GetAsync($"{Base}/{aId}/steps/{stepOfB}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR pai/filho (CA 16)");
        (await engineer.DeleteAsync($"{Base}/{aId}/items/{linkOfB}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var foreignStep = await engineer.PutAsJsonAsync($"{Base}/{aId}", new
        {
            name = a.GetProperty("name").GetString(), productType = (string?)null, version = a.GetProperty("version").GetUInt32(),
            steps = new[] { Step("Corte", "C", assembly, id: stepOfB) },
        });
        foreignStep.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        FieldError(await JsonAsync(foreignStep), "steps[0].id").Should().Be("Etapa não encontrada neste roteiro.");

        var seedKey = await engineer.PostAsJsonAsync(Base, new { name = Unique("Com seed"), productType = (string?)null, seedKey = "example-routing", steps = new[] { Step("Corte", "C", assembly) } });
        seedKey.StatusCode.Should().Be(HttpStatusCode.BadRequest, "campo desconhecido no corpo é recusado (UnmappedMemberHandling.Disallow)");
        var isSystem = await engineer.PostAsJsonAsync(Base, new { name = Unique("Sistema"), productType = (string?)null, steps = new[] { new { id = (Guid?)null, name = "Corte", code = "C", workCenterId = assembly, standardHours = 1m, isQualityStep = false, requiresApproval = false, checklist = Array.Empty<string>(), isSystem = true } } });
        isSystem.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}