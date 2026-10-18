using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Web;
using BemAqui.Identity.Application;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Importing;

public partial class ImportTests
{
    private const string Host = "a.localhost";

    private async Task<HttpClient> ClientAsync(string? seedKey)
    {
        var (id, stamp) = await tenancy.SeedUserAsync(tenancy.TenantA, "m" + Guid.NewGuid().ToString("N")[..10] + "@teste.com");
        if (seedKey is not null)
        {
            await using var scope = await ScopeAsync();
            var db = scope.ServiceProvider.GetRequiredService<IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = await db.Groups.Where(g => g.SeedKey == seedKey).Select(g => g.Id).SingleAsync() });
            await db.SaveChangesAsync();
        }

        var client = tenancy.ClientFor(Host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(tenancy.TenantA.Id, Host, userId: id, stamp: stamp));
        return client;
    }

    [Fact]
    public async Task Tela_lista_execucoes_totais_e_pendencias_com_settings_view()
    {
        var bad = NextRow();
        var source = new FakeSnapshot();
        source.Societe.Add(FakeSnapshot.Company(bad, "Lambda com CNPJ errado", "11111111111111"));
        source.Societe.Add(FakeSnapshot.Company(bad + 1, "Lambda Ltda", NewCnpj()));
        var run = (await RunAsync(source)).Report!.RunId;
        var owner = await ClientAsync(Group.OwnerSeedKey);

        var list = await owner.GetAsync("/api/v1/settings/import-runs");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await list.Content.ReadAsStringAsync());
        list.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await list.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().Should().Contain(r => r.GetProperty("id").GetGuid() == run);

        var detail = await owner.GetFromJsonAsync<JsonElement>("/api/v1/settings/import-runs/" + run);
        var totals = detail.GetProperty("entities")[0];
        totals.GetProperty("read").GetInt32().Should().Be(2);
        totals.GetProperty("errors").GetInt32().Should().Be(1);
        detail.GetProperty("operator").GetString().Should().Be(Operator);

        var pending = await owner.GetFromJsonAsync<JsonElement>("/api/v1/settings/import-runs/" + run + "/pending?kind=error");
        var item = pending.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("sourceId").GetString().Should().Be(bad.ToString(System.Globalization.CultureInfo.InvariantCulture));
        item.GetProperty("kind").GetString().Should().Be("error");
        item.TryGetProperty("payload", out _).Should().BeFalse("o conteúdo da staging não sai por endpoint (CA 22)");

        (await owner.GetAsync("/api/v1/settings/import-runs/" + Guid.CreateVersion7())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var denied = await (await ClientAsync(null)).GetAsync("/api/v1/settings/import-runs");
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("forbidden");
    }

    /// <summary>CA 8 e 19: só leitura, com módulo settings e permissão settings.view em toda rota da importação.</summary>
    [Fact]
    public void Rotas_da_importacao_sao_so_leitura_com_settings_view()
    {
        var endpoints = tenancy.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1/settings/import-runs", StringComparison.Ordinal) == true)
            .ToList();

        endpoints.Should().HaveCount(3);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()!.HttpMethods.Should().Equal(["GET"], endpoint.DisplayName);
            endpoint.Metadata.GetMetadata<RequiresModuleMetadata>()!.ModuleCode.Should().Be("settings");
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().Contain(a => a.Policy == ModuleEndpoints.PermissionPolicyPrefix + "settings.view");
        }
    }
}
