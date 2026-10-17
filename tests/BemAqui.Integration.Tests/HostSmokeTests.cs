using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace BemAqui.Integration.Tests;

[Collection(PostgresTestGroup.Name)]
public class HostSmokeTests(PostgresFixture postgres)
{
    private static WebApplicationFactory<Program> Factory(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Catalog", connectionString);
        });

    [Fact]
    public async Task Testcontainers_sobe_PostgreSQL_17()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        connection.PostgreSqlVersion.Major.Should().Be(17);
    }

    [Fact]
    public async Task Health_responde_200_com_banco_disponivel()
    {
        await using var factory = Factory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Health_responde_503_sem_banco_e_live_continua_200()
    {
        await using var factory = Factory("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");
        using var client = factory.CreateClient();

        var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), TestContext.Current.CancellationToken);
        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        live.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpenApi_nao_e_exposto_fora_de_desenvolvimento()
    {
        await using var factory = Factory(postgres.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
