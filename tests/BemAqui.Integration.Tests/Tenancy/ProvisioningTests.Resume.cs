using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Platform.Application.Provisioning;
using BemAqui.Platform.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Tenancy;

public partial class ProvisioningTests
{
    [Fact]
    public async Task Falha_numa_etapa_deixa_em_Provisioning_e_a_retomada_continua_dali_sem_repetir()
    {
        await using var withSeeder = tenancy.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<ITenantSeeder, FlakySeeder>()));
        FlakySeeder.Calls = 0;
        FlakySeeder.FailNext = true;
        var request = new ProvisionRequest("empresa-r", "Empresa R Ltda", null, "r.localhost", "dono@r.localhost", null);

        var first = await ProvisionAsync(request, withSeeder.Services);

        first.Success.Should().BeFalse();
        first.Error.Should().Contain("Seed").And.Contain("falha proposital");
        first.NextStep.Should().Contain("--resume");
        var tenant = await tenancy.FindAsync("empresa-r");
        tenant.Status.Should().Be(TenantLifecycle.Provisioning);
        (await LogsAsync(tenant.Id)).Should().Contain(l => l.Step == ProvisioningSteps.Seed && l.Status == ProvisioningStatus.Failed);

        var resumed = await ProvisionAsync(request with { Resume = true }, withSeeder.Services);

        resumed.Success.Should().BeTrue(resumed.Error);
        resumed.Steps.Where(s => s.Status == "Já concluída").Select(s => s.Step)
            .Should().Equal(ProvisioningSteps.CreateRole, ProvisioningSteps.CreateDatabase, ProvisioningSteps.Migrate);
        FlakySeeder.Calls.Should().Be(1, "o seed roda uma vez com sucesso, sem duplicar");
        (await tenancy.FindAsync("empresa-r")).Status.Should().Be(TenantLifecycle.Active);

        // Rodar a retomada de novo com tudo concluído não refaz nada.
        var again = await ProvisionAsync(request with { Resume = true }, withSeeder.Services);
        again.Steps.Should().OnlyContain(s => s.Status == "Já concluída");
        FlakySeeder.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Role_do_tenant_nao_tem_privilegios_e_o_banco_nao_aceita_PUBLIC()
    {
        await using var admin = await tenancy.AdminAsync();
        await using var role = new NpgsqlCommand("SELECT rolsuper, rolcreatedb, rolcreaterole, rolreplication FROM pg_roles WHERE rolname = $1", admin)
        {
            Parameters = { new() { Value = tenancy.TenantA.DbName } },
        };
        await using (var reader = await role.ExecuteReaderAsync())
        {
            (await reader.ReadAsync()).Should().BeTrue("o role do tenant existe");
            Enumerable.Range(0, 4).Select(reader.GetBoolean).Should().AllBeEquivalentTo(false);
        }

        await using var publicConnect = new NpgsqlCommand(
            "SELECT count(*) FROM pg_database d, aclexplode(d.datacl) a WHERE d.datname = $1 AND a.grantee = 0 AND a.privilege_type = 'CONNECT'", admin)
        {
            Parameters = { new() { Value = tenancy.TenantA.DbName } },
        };
        ((long)(await publicConnect.ExecuteScalarAsync())!).Should().Be(0, "REVOKE CONNECT FROM PUBLIC");

        await using var owner = new NpgsqlCommand("SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname = $1", admin)
        {
            Parameters = { new() { Value = tenancy.TenantA.DbName } },
        };
        ((string)(await owner.ExecuteScalarAsync())!).Should().Be(tenancy.TenantA.DbName, "o role do tenant é dono só do banco dele");
    }

    [Fact]
    public async Task Provisionador_nao_e_superuser()
    {
        await using var admin = await tenancy.AdminAsync();
        await using var command = new NpgsqlCommand("SELECT rolsuper, rolcreatedb, rolcreaterole FROM pg_roles WHERE rolname = 'bemaqui_provisioner'", admin);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        reader.GetBoolean(0).Should().BeFalse();
        reader.GetBoolean(1).Should().BeTrue();
        reader.GetBoolean(2).Should().BeTrue();
    }

    [Fact]
    public async Task Senha_do_role_e_forte_fica_so_cifrada_e_nao_aparece_nos_logs()
    {
        var connection = tenancy.Decrypt(tenancy.TenantA);
        var password = new NpgsqlConnectionStringBuilder(connection).Password!;

        password.Length.Should().BeGreaterThanOrEqualTo(64, "32 bytes aleatórios em hexadecimal");
        tenancy.TenantA.ConnectionSecret.Should().NotContain(password);
        (await LogsAsync(tenancy.TenantA.Id)).Should().OnlyContain(l => l.Message == null || !l.Message.Contains(password));
    }
}
