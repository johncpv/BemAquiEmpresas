using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BemAqui.BuildingBlocks.Security;
using BemAqui.Host.Cli;
using BemAqui.Host.Persistence;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-03a CA 9 a 12: migrator com relatório, falha isolada, lock e 503 com schema defasado.</summary>
public class MigratorTests(TenancyFixture tenancy) : IClassFixture<TenancyFixture>
{
    private Task<int> RunCliAsync(TextWriter output, params string[] args) =>
        CliCommand.RunAsync(args, c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = tenancy.CatalogConnection,
            ["ConnectionStrings:Provisioner"] = tenancy.ProvisionerConnection,
            ["DataProtection:CertificatePath"] = TestCertificates.Primary,
            ["DataProtection:CertificatePassword"] = TestCertificates.Password,
            ["DataProtection:AllowKeyRingCreation"] = "false",
        }), output);

    [Fact]
    public async Task Migrate_aplica_em_todos_e_falha_em_um_nao_bloqueia_os_outros()
    {
        var broken = await tenancy.ProvisionAsync("empresa-x", "Empresa X", "x.localhost");
        var protector = tenancy.Factory.Services.GetRequiredService<ISecretProtector>();
        await tenancy.UpdateTenantAsync(broken.Id, t => t.ConnectionSecret = protector.Protect(
            SecretScope.ForTenant(t.Id), SecretKind.TenantConnection, new Secret("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2")));
        var output = new StringWriter();

        var exitCode = await RunCliAsync(output, "migrate");

        exitCode.Should().Be(2);
        var report = output.ToString();
        report.Should().Contain("OK").And.Contain("empresa-a").And.Contain("empresa-b");
        report.Should().Contain("FALHOU empresa-x");
        report.Should().NotContain("Password=");
        (await tenancy.FindAsync("empresa-a")).SchemaVersion.Should().Be(TenantSchema.ExpectedVersion);
    }

    [Fact]
    public async Task CLI_provisiona_e_a_saida_nao_mostra_a_senha()
    {
        var output = new StringWriter();

        var exitCode = await RunCliAsync(output, "tenant", "provision", "--key", "empresa-cli", "--legal-name", "Empresa CLI Ltda",
            "--host", "cli.localhost", "--owner-email", "dono@cli.localhost");

        exitCode.Should().Be(0, output.ToString());
        var password = new NpgsqlConnectionStringBuilder(tenancy.Decrypt(await tenancy.FindAsync("empresa-cli"))).Password!;
        output.ToString().Should().Contain("Tenant provisionado").And.Contain("https://cli.localhost").And.NotContain(password);
    }

    [Fact]
    public async Task Dois_migrators_em_paralelo_nao_migram_o_mesmo_banco_ao_mesmo_tempo()
    {
        await using var admin = await tenancy.AdminAsync();
        var database = "paralelo_" + Guid.NewGuid().ToString("N")[..8];
        await using (var create = new NpgsqlCommand("CREATE DATABASE " + database, admin))
        {
            await create.ExecuteNonQueryAsync();
        }

        var connection = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database, Password = new NpgsqlConnectionStringBuilder(tenancy.CatalogConnection).Password }.ConnectionString;
        var migrator = new TenantDatabaseMigrator();

        var results = await Task.WhenAll(
            Task.Run(() => migrator.MigrateAsync(connection, CancellationToken.None)),
            Task.Run(() => migrator.MigrateAsync(connection, CancellationToken.None)));

        results.Should().AllSatisfy(r => r.To.Should().Be(TenantSchema.ExpectedVersion));
        results.Count(r => r.From is null).Should().Be(1, "o segundo esperou o lock e encontrou o banco já migrado");
    }

    [Fact]
    public async Task Tenant_com_schema_defasado_recebe_503_ate_migrar()
    {
        var client = tenancy.ClientFor("b.localhost");
        await tenancy.UpdateTenantAsync(tenancy.TenantB.Id, t => t.SchemaVersion = "20000101000000_Antiga");

        var watch = Stopwatch.StartNew();
        HttpResponseMessage response;
        do
        {
            response = await client.GetAsync("/api/v1/me");
        }
        while (response.StatusCode != HttpStatusCode.ServiceUnavailable && watch.Elapsed < TimeSpan.FromSeconds(10) && await Task.Delay(200).ContinueWith(_ => true));

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString().Should().Be("tenant_schema_outdated");

        await tenancy.UpdateTenantAsync(tenancy.TenantB.Id, t => t.SchemaVersion = TenantSchema.ExpectedVersion);
    }
}
