using System.Net;
using BemAqui.Host.Health;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace BemAqui.Integration.Tests;

[Collection(PostgresTestGroup.Name)]
public class HostSmokeTests(PostgresFixture postgres)
{
    internal static WebApplicationFactory<Program> Factory(string connectionString, bool allowKeyRingCreation = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:Catalog", connectionString);
            b.UseSetting("DataProtection:CertificatePath", TestCertificates.Primary);
            b.UseSetting("DataProtection:CertificatePassword", TestCertificates.Password);
            b.UseSetting("DataProtection:AllowKeyRingCreation", allowKeyRingCreation.ToString());
            b.UseSetting("DataProtection:AutoMigrate", "true");
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
        var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        live.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Host_nao_sobe_sem_o_key_ring()
    {
        var db = await postgres.CreateDatabaseAsync("host_sem_keys");
        await using var factory = Factory(db, allowKeyRingCreation: false);

        var subir = () => factory.CreateClient();

        subir.Should().Throw<InvalidOperationException>().WithMessage("*Key ring*vazio*");
    }

    [Fact]
    public async Task Health_check_do_catalogo_acusa_banco_indisponivel()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Catalog"] = "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2" })
            .Build();

        var result = await new PostgresHealthCheck(configuration).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Unhealthy);
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
