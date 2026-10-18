using System.Diagnostics;
using System.Net;
using BemAqui.Identity.Domain;
using FluentAssertions;
using Npgsql;

namespace BemAqui.Integration.Tests.Tenancy;

/// <summary>FND-09c CA 5: página de uma lista com 50 mil registros em menos de 1 s (tabela real: audit.audit_log).</summary>
public partial class AuthTests
{
    [Fact]
    public async Task Pagina_com_50_mil_registros_responde_em_menos_de_1_segundo()
    {
        var owner = await ActorAsync(Group.OwnerSeedKey);
        var entityId = Guid.NewGuid().ToString();
        await using (var connection = new NpgsqlConnection(tenancy.Decrypt(tenancy.TenantA)))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO audit.audit_log (id, entity, entity_id, action, user_id, at, changes, ip)
                SELECT gen_random_uuid(), 'Group', @id, 'Updated', NULL, now() - (n || ' seconds')::interval, '{"name":{"old":"a","new":"b"}}'::jsonb, NULL
                FROM generate_series(1, 50000) AS n
                """, connection);
            insert.Parameters.AddWithValue("id", entityId);
            (await insert.ExecuteNonQueryAsync()).Should().Be(50_000);
        }

        var url = $"/api/v1/audit/Group/{entityId}?page=2400&pageSize=20";
        (await owner.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK); // aquece conexão e cache de permissão

        var watch = Stopwatch.StartNew();
        var response = await owner.Client.GetAsync(url);
        watch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await JsonAsync(response);
        body.GetProperty("total").GetInt32().Should().Be(50_000);
        body.GetProperty("items").GetArrayLength().Should().Be(20);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));

        var nobody = await ActorAsync();
        (await nobody.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "histórico exige identity.view");
    }
}
