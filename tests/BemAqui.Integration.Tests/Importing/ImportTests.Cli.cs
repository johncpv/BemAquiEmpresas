using BemAqui.Host.Cli;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace BemAqui.Integration.Tests.Importing;

public partial class ImportTests
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

    private static string[] Import(string tenant, string confirm) =>
        ["tenant", "import-dolibarr", "--tenant", tenant, "--confirm-tenant", confirm, "--snapshot", "/nao/existe", "--entities", "parties", "--operator", Operator];

    [Fact]
    public async Task CLI_recusa_tenant_errado_inexistente_suspenso_ou_defasado()
    {
        var output = new StringWriter();
        (await RunCliAsync(output, Import("empresa-a", "empresa-b"))).Should().Be(1);
        output.ToString().Should().Contain("--confirm-tenant precisa repetir a chave");

        output = new StringWriter();
        (await RunCliAsync(output, Import("nao-existe", "nao-existe"))).Should().Be(1);
        output.ToString().Should().Contain("Tenant não encontrado");

        var tenant = await tenancy.ProvisionAsync("empresa-importa", "Empresa Importa", "importa.localhost");
        await tenancy.SetStatusAsync(tenant.Id, suspended: true);
        output = new StringWriter();
        (await RunCliAsync(output, Import("empresa-importa", "empresa-importa"))).Should().Be(1);
        output.ToString().Should().Contain("suspenso");

        await tenancy.SetStatusAsync(tenant.Id, suspended: false);
        await tenancy.UpdateTenantAsync(tenant.Id, t => t.SchemaVersion = "00000000000000_Antiga");
        output = new StringWriter();
        (await RunCliAsync(output, Import("empresa-importa", "empresa-importa"))).Should().Be(1);
        output.ToString().Should().Contain("schema defasado").And.Contain("Nada foi gravado");

        await tenancy.UpdateTenantAsync(tenant.Id, t => t.SchemaVersion = BemAqui.Host.Persistence.TenantSchema.ExpectedVersion);
        output = new StringWriter();
        (await RunCliAsync(output, Import("empresa-importa", "empresa-importa"))).Should().Be(2, output.ToString());
        output.ToString().Should().Contain("Arquivo de secret da conexão do snapshot não encontrado");
    }
}
