using System.Text.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.BuildingBlocks.Tests;

/// <summary>FND-06 CA 4: módulo não liberado responde 403 <c>module_not_released</c> com a mensagem padrão.</summary>
public class ModuleReleaseFilterTests
{
    private static DefaultHttpContext Context(params string[] released)
    {
        var holder = new TenantContextHolder();
        holder.Set(new ResolvedTenant(new TenantInfo(Guid.NewGuid(), "t", TenantStatus.Active), "T", null, "t.localhost", "x")
        {
            ReleasedModules = released.ToHashSet(StringComparer.Ordinal),
        });
        var services = new ServiceCollection().AddSingleton(holder).AddLogging().AddProblemDetails().BuildServiceProvider();
        return new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
    }

    [Fact]
    public void Liberado_passa_e_nao_liberado_nao()
    {
        ModuleEndpoints.IsReleased(Context("finance"), "finance").Should().BeTrue();
        ModuleEndpoints.IsReleased(Context("finance"), "hr").Should().BeFalse();
        ModuleEndpoints.IsReleased(new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() }, "finance").Should().BeFalse();
    }

    [Fact]
    public async Task Resposta_e_403_module_not_released_com_a_mensagem_padrao()
    {
        var http = Context();

        await ModuleEndpoints.NotReleased("hr").ExecuteAsync(http);

        http.Response.StatusCode.Should().Be(403);
        http.Response.Body.Position = 0;
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(http.Response.Body);
        body.GetProperty("code").GetString().Should().Be("module_not_released");
        body.GetProperty("detail").GetString().Should().Be("Funcionalidade não liberada nessa versão do sistema.");
    }

    [Fact]
    public void Modulo_fora_do_catalogo_nao_pode_ser_exigido()
    {
        var act = () => new FakeConventionBuilder().RequireModule("crm");
        act.Should().Throw<ArgumentException>();
    }

    private sealed class FakeConventionBuilder : Microsoft.AspNetCore.Builder.IEndpointConventionBuilder
    {
        public void Add(Action<Microsoft.AspNetCore.Builder.EndpointBuilder> convention)
        {
        }
    }
}
