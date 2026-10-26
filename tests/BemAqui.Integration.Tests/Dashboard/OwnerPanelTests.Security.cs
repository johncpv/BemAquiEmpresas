using System.Net;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Dashboard.Application;
using BemAqui.Platform.Infrastructure.Catalog;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Dashboard;

/// <summary>DSH-01b CA 2 e 11: card por permissão e módulo do dono, 403 com o código da camada e DTO com custo só nas rotas com <c>projects.view_cost</c>.</summary>
public partial class OwnerPanelTests
{
    private static readonly string[] CostRoutes =
        [Base + "/cards/margin", Base + "/cards/active-projects/margins", Base + "/project-results", Base + "/project-results/" + Guid.Empty];

    private async Task SetReleasedAsync(string module, bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().TenantModules.Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == module)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<ITenantResolver>().Invalidate();
    }

    [Fact]
    public async Task Sem_view_cost_nao_ha_card_Margem_nem_coluna_de_margem_nem_resultado_por_projeto()
    {
        var viewer = await ActorWithAsync("dashboard.view", "projects.view", "finance.view", "production.view");
        var cards = (await OkAsync(viewer, Base)).GetProperty("cards").EnumerateArray().Select(c => c.GetString()).ToList();
        cards.Should().Contain(["active-projects", "backlog", "deliveries", "production"]).And.NotContain("margin");
        foreach (var url in CostRoutes)
        {
            (await StatusAsync(viewer, url)).Should().Be((HttpStatusCode.Forbidden, "forbidden"), url);
        }

        // Com view_cost e sem finance.view: o card Margem sim, o resultado por projeto não.
        var cost = await ActorWithAsync("dashboard.view", "projects.view", "projects.view_cost");
        var withCost = (await OkAsync(cost, Base)).GetProperty("cards").EnumerateArray().Select(c => c.GetString()).ToList();
        withCost.Should().Contain("margin").And.NotContain("production");
        (await StatusAsync(cost, Base + "/cards/margin")).Status.Should().Be(HttpStatusCode.OK);
        (await StatusAsync(cost, Base + "/project-results")).Should().Be((HttpStatusCode.Forbidden, "forbidden"), "o resultado por projeto exige finance.view");
        (await StatusAsync(cost, Base + "/cards/active-projects/received")).Should().Be((HttpStatusCode.Forbidden, "forbidden"));
        (await StatusAsync(cost, Base + "/cards/production")).Should().Be((HttpStatusCode.Forbidden, "forbidden"));

        // Sem dashboard.view, nada do painel.
        var noDashboard = await ActorWithAsync("projects.view", "projects.view_cost", "finance.view");
        (await StatusAsync(noDashboard, Base + "/project-results")).Should().Be((HttpStatusCode.Forbidden, "forbidden"));
    }

    [Fact]
    public async Task Modulo_do_card_nao_liberado_nao_monta_o_card_e_responde_module_not_released()
    {
        var owner = await OwnerAsync();
        try
        {
            await SetReleasedAsync("production", false);
            (await OkAsync(owner, Base)).GetProperty("cards").EnumerateArray().Select(c => c.GetString()).Should().NotContain("production");
            (await StatusAsync(owner, Base + "/cards/production")).Should().Be((HttpStatusCode.Forbidden, "module_not_released"));
            await SetReleasedAsync("production", true);

            await SetReleasedAsync("projects", false);
            var cards = (await OkAsync(await OwnerAsync(), Base)).GetProperty("cards").EnumerateArray().Select(c => c.GetString()).ToList();
            cards.Should().NotContain(["margin", "active-projects", "backlog", "deliveries"]);
            foreach (var url in new[] { Base + "/cards/margin", Base + "/cards/active-projects", Base + "/cards/backlog", Base + "/cards/deliveries", Base + "/project-results" })
            {
                (await StatusAsync(owner, url)).Should().Be((HttpStatusCode.Forbidden, "module_not_released"), url);
            }

            await SetReleasedAsync("projects", true);
            await SetReleasedAsync("finance", false);
            (await StatusAsync(owner, Base + "/project-results")).Should().Be((HttpStatusCode.Forbidden, "module_not_released"), "o resultado por projeto também é do Finance");
            (await StatusAsync(owner, Base + "/cards/active-projects/received")).Should().Be((HttpStatusCode.Forbidden, "module_not_released"));
        }
        finally
        {
            await SetReleasedAsync("production", true);
            await SetReleasedAsync("projects", true);
            await SetReleasedAsync("finance", true);
        }
    }

    [Fact]
    public void Rotas_que_devolvem_DTO_com_custo_exigem_projects_view_cost()
    {
        // ADR 0028 §2: DTO por forma de permissão. Toda rota do painel cujo 200 é um tipo [CostDto] (ou lista dele) tem a policy perm:projects.view_cost.
        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith(Base, StringComparison.Ordinal) == true).ToList();
        static Type? Ok(RouteEndpoint e)
        {
            var type = e.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().FirstOrDefault(m => m.StatusCode == StatusCodes.Status200OK)?.Type;
            return type is { IsGenericType: true } ? type.GetGenericArguments()[0] : type;
        }

        var costly = endpoints.Where(e => Ok(e) is { } t && t.GetCustomAttributes(typeof(CostDtoAttribute), false).Length > 0).ToList();
        costly.Select(e => e.RoutePattern.RawText!.TrimEnd('/')).Should().Contain([Base + "/cards/margin", Base + "/cards/active-projects/margins", Base + "/project-results",
            Base + "/project-results/{projectId:guid}", Base + "/project-results/{projectId:guid}/cell"]);
        costly.Should().OnlyContain(e => e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == ModuleEndpoints.PermissionPolicyPrefix + DashboardCards.CostPermission));
        endpoints.Where(e => !costly.Contains(e)).Select(Ok).Where(t => t is not null)
            .Should().OnlyContain(t => t!.GetCustomAttributes(typeof(CostDtoAttribute), false).Length == 0);
    }
}
