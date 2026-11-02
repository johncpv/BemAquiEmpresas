using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Integration.Tests.Tenancy;
using BemAqui.Platform.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Npgsql;

namespace BemAqui.Integration.Tests.GoLive;

/// <summary>
/// GOL-01a CA 10, GOL-01b CA 4: o <c>acessos-virada.sql</c> derruba toda sessão de antes (refresh e JWT dão 401), apaga os usuários de teste,
/// não grava nada na simulação e recusa deixar o Proprietário sem membro ativo.
/// </summary>
[Collection(GoLiveGroup.Name)]
public class AcessosViradaTests(GoLiveFixture golive)
{
    private static HttpRequestMessage Refresh(string host, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("Cookie", "__Host-bq_rt=" + cookie);
        request.Headers.Add("X-CSRF", "1");
        request.Headers.Add("Origin", "https://" + host);
        return request;
    }

    private static string DatabaseOf(string connection) => new NpgsqlConnectionStringBuilder(connection).Database!;

    private async Task<(Tenant Tenant, string Connection, string Host)> TenantAsync(string key)
    {
        var tenant = await golive.ProvisionAsync(key);
        return (tenant, golive.Tenancy.Decrypt(tenant), key + ".homolog.localhost");
    }

    private static async Task<long> ScalarAsync(string connection, string sql, params object[] values)
    {
        await using var db = await GoLiveFixture.OpenAsync(connection);
        await using var command = new NpgsqlCommand(sql, db);
        foreach (var value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static Task<long> MakeOwnerAsync(string connection, Guid userId) =>
        ScalarAsync(connection, "WITH i AS (INSERT INTO identity.user_groups (id, user_id, group_id) SELECT gen_random_uuid(), $1, id FROM identity.groups WHERE is_system AND seed_key = 'owner' RETURNING 1) SELECT count(*) FROM i", userId);

    [Fact]
    public async Task Refresh_e_JWT_emitidos_antes_do_script_dao_401_depois_e_os_usuarios_de_teste_somem()
    {
        var (tenant, connection, host) = await TenantAsync("acessos");
        var (ownerId, _) = await golive.Tenancy.SeedUserAsync(tenant, "dono@acessos.com.br");
        await MakeOwnerAsync(connection, ownerId);
        var email = "operador@acessos.com.br";
        await golive.Tenancy.SeedUserAsync(tenant, email);
        await golive.Tenancy.SeedUserAsync(tenant, "qa1@teste.invalid");
        await golive.Tenancy.SeedUserAsync(tenant, "avulso@acessos.com.br");

        var login = await golive.Tenancy.ClientFor(host).PostAsJsonAsync("/api/v1/auth/login", new { email, password = TenancyFixture.DefaultPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var jwt = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-bq_rt=", StringComparison.Ordinal)).Split(';')[0]["__Host-bq_rt=".Length..];
        var settings = new Dictionary<string, string> { ["bqe.dominios_teste"] = "teste.invalid", ["bqe.emails_teste"] = "avulso@acessos.com.br" };

        var dry = await GoLiveFixture.RunScriptAsync(connection, "acessos-virada.sql", settings);
        dry.Single().Rows.Should().OnlyContain(r => ((string)r[2]!).StartsWith("simulação", StringComparison.Ordinal));
        dry.Single().Rows.Single(r => (string)r[0]! == "Usuários de teste apagados")[1].Should().Be(2L);
        (await ScalarAsync(connection, "SELECT count(*) FROM identity.users WHERE normalized_email IN ('qa1@teste.invalid', 'avulso@acessos.com.br')")).Should().Be(2, "a simulação não apaga");
        (await ScalarAsync(connection, "SELECT count(*) FROM identity.refresh_tokens WHERE revoked_at IS NULL")).Should().BeGreaterThan(0, "a simulação não revoga");

        var applied = await GoLiveFixture.RunScriptAsync(connection, "acessos-virada.sql", new Dictionary<string, string>(settings) { ["bqe.confirmar"] = DatabaseOf(connection) });
        applied.Single().Rows.Should().OnlyContain(r => (string)r[2]! == "aplicado");
        (await ScalarAsync(connection, "SELECT count(*) FROM identity.users WHERE normalized_email IN ('qa1@teste.invalid', 'avulso@acessos.com.br')")).Should().Be(0);
        (await ScalarAsync(connection, "SELECT count(*) FROM identity.refresh_tokens WHERE revoked_at IS NULL")).Should().Be(0);

        // A API sobe de novo depois do script (runbook): o cache do security stamp é do processo.
        await using var restarted = HostSmokeTests.Factory(golive.Tenancy.CatalogConnection)
            .WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Provisioner", golive.Tenancy.ProvisionerConnection));
        var client = restarted.CreateClient();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Authorization = new("Bearer", jwt);
        (await client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o JWT de antes tem o sstamp antigo");
        client.DefaultRequestHeaders.Authorization = null;
        // A renovação recusada responde 422 auth.session_expired (contrato atual do /auth/refresh, FND-04): o front leva ao login.
        var refused = await client.SendAsync(Refresh(host, cookie));
        refused.IsSuccessStatusCode.Should().BeFalse("a família de refresh foi revogada");
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("auth.session_expired");

        var again = await GoLiveFixture.RunScriptAsync(connection, "acessos-virada.sql", new Dictionary<string, string>(settings) { ["bqe.confirmar"] = DatabaseOf(connection) });
        again.Single().Rows.Where(r => (string)r[0]! is "Usuários de teste apagados" or "Famílias de refresh revogadas").Should().OnlyContain(r => (long)r[1]! == 0, "idempotente");
    }

    [Fact]
    public async Task Recusa_quando_o_Proprietario_ficaria_sem_membro_ativo()
    {
        var (tenant, connection, _) = await TenantAsync("acessos-dono");
        var (ownerId, _) = await golive.Tenancy.SeedUserAsync(tenant, "dono@teste.invalid");
        await MakeOwnerAsync(connection, ownerId);

        var run = () => GoLiveFixture.RunScriptAsync(connection, "acessos-virada.sql",
            new Dictionary<string, string> { ["bqe.dominios_teste"] = "teste.invalid", ["bqe.confirmar"] = DatabaseOf(connection) });

        (await run.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("BQOWN");
        (await ScalarAsync(connection, "SELECT count(*) FROM identity.users WHERE normalized_email = 'dono@teste.invalid'")).Should().Be(1, "nada foi gravado");
    }
}
