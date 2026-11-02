using BemAqui.Platform.Contracts;
using FluentAssertions;
using Npgsql;

namespace BemAqui.Integration.Tests.GoLive;

/// <summary>
/// GOL-01a CA 3 e 6, ADR 0034 §2: <c>tenant promote</c> leva só o tenant pedido para o catálogo novo, com o mesmo id, os módulos e o host
/// principal informado, com cobrança real e ambiente fiscal desligados; simulação não grava e rodar de novo não muda nada.
/// </summary>
[Collection(GoLiveGroup.Name)]
public class GoLivePromoteTests(GoLiveFixture golive)
{
    private async Task<long> CountAsync(string sql, params object[] values)
    {
        await using var db = await GoLiveFixture.OpenAsync(golive.NewCatalog);
        await using var command = new NpgsqlCommand(sql, db);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Promove_so_o_tenant_pedido_com_o_mesmo_id_e_as_flags_desligadas_e_e_idempotente()
    {
        var tenant = await golive.ProvisionAsync("promove");
        await golive.Tenancy.UpdateTenantAsync(tenant.Id, t =>
        {
            t.BankProductionAllowed = true;
            t.FiscalEnvironmentAllowed = FiscalEnvironmentAllowance.Homologation;
        });
        var oldModules = await CountOldAsync("SELECT count(*) FROM catalog.tenant_modules WHERE tenant_id = $1", tenant.Id);
        var dry = new StringWriter();

        (await golive.RunNewAsync(dry, "tenant", "promote", "--tenant", "promove", "--from-catalog-file", golive.OldCatalogFile,
            "--primary-host", "app.promove.localhost", "--dry-run")).Should().Be(0, dry.ToString());
        (await CountAsync("SELECT count(*) FROM catalog.tenants WHERE id = $1", tenant.Id)).Should().Be(0, "a simulação não grava");
        (await golive.RunNewAsync(new StringWriter(), "tenant", "promote", "--tenant", "promove", "--from-catalog-file", golive.OldCatalogFile,
            "--primary-host", "app.promove.localhost")).Should().Be(1, "gravar exige --confirm-tenant");

        var first = new StringWriter();
        (await golive.PromoteAsync("promove", first)).Should().Be(0, first.ToString());
        (await golive.PromoteAsync("promove")).Should().Be(0, "rodar de novo não falha");

        (await CountAsync("SELECT count(*) FROM catalog.tenants WHERE key IN ('empresa-a', 'empresa-b')")).Should().Be(0,
            "nenhum outro tenant da homologação vai para o catálogo novo");
        (await CountAsync("SELECT count(*) FROM catalog.tenants WHERE id = $1 AND key = 'promove' AND NOT bank_production_allowed AND fiscal_environment_allowed = 'None' AND webhook_key IS NULL AND status = 'Active'", tenant.Id))
            .Should().Be(1);
        (await CountAsync("SELECT count(*) FROM catalog.tenant_domains WHERE tenant_id = $1", tenant.Id)).Should().Be(1, "o host da homologação fica para trás");
        (await CountAsync("SELECT count(*) FROM catalog.tenant_domains WHERE tenant_id = $1 AND host = 'app.promove.localhost' AND is_primary", tenant.Id)).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM catalog.tenant_modules WHERE tenant_id = $1", tenant.Id)).Should().Be(oldModules).And.BeGreaterThan(0);
        first.ToString().Should().Contain("promove.homolog.localhost", "o relatório mostra os hosts antigos não copiados").And.NotContain("Password=");
    }

    private async Task<long> CountOldAsync(string sql, Guid id)
    {
        await using var db = await GoLiveFixture.OpenAsync(golive.Tenancy.CatalogConnection);
        await using var command = new NpgsqlCommand(sql, db) { Parameters = { new() { Value = id } } };
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
