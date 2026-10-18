using BemAqui.BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Parties;

public partial class PartiesTests
{
    /// <summary>CA 28: todo endpoint do módulo passa pelas três camadas (RequireModule e uma policy perm:).</summary>
    [Fact]
    public void Todo_endpoint_de_parties_tem_RequireModule_e_policy_perm()
    {
        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/parties", StringComparison.Ordinal) == true).ToList();

        endpoints.Should().HaveCountGreaterThan(10);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>().Should().NotBeNull(endpoint.DisplayName).And.Subject.As<RequiresModuleMetadata>().ModuleCode.Should().Be("parties");
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy != null && a.Policy.StartsWith(ModuleEndpoints.PermissionPolicyPrefix + "parties.", StringComparison.Ordinal), endpoint.DisplayName);
        }
    }
}
