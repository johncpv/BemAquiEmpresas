using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.BuildingBlocks.Notifications;
using BemAqui.BuildingBlocks.Persistence;
using BemAqui.BuildingBlocks.Tenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-02 CA 1, 2, 3, 4, 5, 7 e 8: tenant pelo host, banco isolado, suspensão em menos de 10 s.</summary>
public class TenantResolutionTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Cada_host_resolve_o_proprio_tenant()
    {
        var a = await tenancy.ClientFor("a.localhost").GetFromJsonAsync<JsonElement>("/api/v1/public/tenant-context");
        var b = await tenancy.ClientFor("B.LocalHost:8443").GetFromJsonAsync<JsonElement>("/api/v1/public/tenant-context");

        a.GetProperty("name").GetString().Should().Be("Empresa A Ltda");
        b.GetProperty("name").GetString().Should().Be("Empresa B Ltda", "host normalizado: minúsculas e sem porta");
    }

    [Fact]
    public async Task Gravado_no_tenant_A_nao_aparece_no_tenant_B()
    {
        var scopes = tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>();
        var directory = tenancy.Factory.Services.GetRequiredService<ITenantDirectory>();
        var a = (await directory.FindAsync(tenancy.TenantA.Id))!;
        var b = (await directory.FindAsync(tenancy.TenantB.Id))!;

        await using (var scope = await scopes.CreateScopeAsync(a))
        {
            scope.ServiceProvider.GetRequiredService<INotificationService>().Notify(Guid.CreateVersion7(), "teste", "Só no A", "/app").IsSuccess.Should().BeTrue();
            await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().SaveChangesAsync();
        }

        await using (var scope = await scopes.CreateScopeAsync(b))
        {
            (await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().Notifications.CountAsync(n => n.Title == "Só no A")).Should().Be(0);
        }

        await using (var scope = await scopes.CreateScopeAsync(a))
        {
            (await scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().Notifications.CountAsync(n => n.Title == "Só no A")).Should().Be(1);
        }
    }

    [Theory]
    [InlineData("desconhecido.localhost")]
    [InlineData("plataforma.bemaquiapp.com.br")]
    public async Task Host_fora_do_catalogo_ou_da_plataforma_da_404_sem_revelar_nada(string host)
    {
        var response = await tenancy.ClientFor(host).GetAsync("/api/v1/notifications");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("tenant").And.NotContain("Empresa");
    }

    [Fact]
    public async Task Health_responde_em_qualquer_host()
    {
        (await tenancy.ClientFor("qualquer.localhost").GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suspensao_vale_em_menos_de_10_segundos_e_reativacao_tambem()
    {
        var client = tenancy.ClientFor("b.localhost");
        (await client.GetAsync("/api/v1/notifications")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "ativo: chega na autenticação");

        await tenancy.SetStatusAsync(tenancy.TenantB.Id, suspended: true);
        var watch = Stopwatch.StartNew();
        string? code = null;
        while (watch.Elapsed < TimeSpan.FromSeconds(10))
        {
            var response = await client.GetAsync("/api/v1/notifications");
            if (response.StatusCode == HttpStatusCode.Forbidden && (code = (await JsonAsync(response)).GetProperty("code").GetString()) == "tenant_suspended")
            {
                break;
            }

            await Task.Delay(200);
        }

        code.Should().Be("tenant_suspended");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));

        var context = await client.GetFromJsonAsync<JsonElement>("/api/v1/public/tenant-context");
        context.GetProperty("status").GetString().Should().Be("Suspended", "o front usa isso para mostrar a página Acesso suspenso");

        await tenancy.SetStatusAsync(tenancy.TenantB.Id, suspended: false);
        watch.Restart();
        while (watch.Elapsed < TimeSpan.FromSeconds(10)
               && (await client.GetAsync("/api/v1/notifications")).StatusCode == HttpStatusCode.Forbidden)
        {
            await Task.Delay(200);
        }

        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Credencial_do_tenant_A_nao_conecta_no_banco_do_tenant_B()
    {
        await using var connection = new NpgsqlConnection(tenancy.RoleAConnectionToB);

        var conectar = () => connection.OpenAsync();

        (await conectar.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task TenantDbContext_fora_de_escopo_de_tenant_falha_em_vez_de_usar_um_banco_padrao()
    {
        await using var scope = tenancy.Factory.Services.CreateAsyncScope();

        var usar = () => scope.ServiceProvider.GetRequiredService<BemAquiDbContext>().Notifications.CountAsync();

        (await usar.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*fora de um escopo de tenant*");
    }
}
