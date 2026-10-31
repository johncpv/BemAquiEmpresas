using System.Diagnostics;
using System.Net.Http.Json;
using BemAqui.BuildingBlocks.Tenancy;
using BemAqui.Identity.Domain;
using BemAqui.Integration.Tests.Tenancy;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BemAqui.Integration.Tests.Parties;

public partial class PartiesTests
{
    [Fact]
    public async Task Busca_por_nome_sem_acento_CNPJ_parcial_e_CPF_so_inteiro_com_CPF_mascarado_na_lista()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var cnpj = NewCnpj();
        var cpf = NewCpf();
        await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PJ", document = cnpj, legalName = $"Elevação {tag} Ltda", roles = new[] { "Customer" } }));
        await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PF", document = cpf, legalName = $"José {tag}", roles = new[] { "Installer" } }));

        var byName = await JsonAsync(await owner.GetAsync($"/api/v1/parties?q=elevacao%20{tag}"));
        byName.GetProperty("total").GetInt32().Should().Be(1);
        (await JsonAsync(await owner.GetAsync("/api/v1/parties?q=" + cnpj[2..9]))).GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("legalName").GetString() == $"Elevação {tag} Ltda");

        (await JsonAsync(await owner.GetAsync("/api/v1/parties?q=" + cpf[2..9]))).GetProperty("items").EnumerateArray().Should().NotContain(i => i.GetProperty("legalName").GetString() == $"José {tag}", "parte de um CPF não encontra nada (CA 17)");
        var byCpf = await JsonAsync(await owner.GetAsync($"/api/v1/parties?q={cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}"));
        var person = byCpf.GetProperty("items").EnumerateArray().Single();
        person.GetProperty("document").GetString().Should().Be($"***.{cpf[3..6]}.{cpf[6..9]}-**");
        person.GetProperty("roles")[0].GetString().Should().Be("Installer");

        var full = await JsonAsync(await owner.GetAsync("/api/v1/parties/" + person.GetProperty("id").GetGuid()));
        full.GetProperty("document").GetString().Should().Be($"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}");

        (await JsonAsync(await owner.GetAsync("/api/v1/parties?role=Installer&q=" + tag))).GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Busca_da_topbar_comeca_em_3_caracteres_e_traz_ate_5_com_CPF_mascarado()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var tag = "tb" + Guid.NewGuid().ToString("N")[..5];
        foreach (var i in Enumerable.Range(0, 7))
        {
            await CreatedAsync(await owner.PostAsJsonAsync("/api/v1/parties", new { kind = "PF", document = NewCpf(), legalName = $"Topbar {tag} {i}", roles = new[] { "Customer" } }));
        }

        (await JsonAsync(await owner.GetAsync("/api/v1/parties/search?q=to"))).GetArrayLength().Should().Be(0);
        var hits = await JsonAsync(await owner.GetAsync("/api/v1/parties/search?q=" + tag));
        hits.GetArrayLength().Should().Be(5);
        hits[0].GetProperty("document").GetString().Should().StartWith("***.");
    }

    [Fact]
    [Trait(LoadTest.Trait, LoadTest.Value)]
    public async Task Com_50_mil_terceiros_a_busca_por_parte_do_nome_responde_em_menos_de_1_segundo()
    {
        var perf = await tenancy.ProvisionAsync("parties-perf", "Desempenho Ltda", "perf.localhost");
        await using (var connection = new NpgsqlConnection(tenancy.Decrypt(perf)))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand("""
                INSERT INTO parties.parties (id, kind, document, legal_name, trade_name, search_text, tax_regime, is_active, created_at, is_deleted)
                SELECT gen_random_uuid(), 'PJ', NULL, 'Indústria Metalúrgica ' || g, 'Metal ' || g, 'industria metalurgica ' || g || ' metal ' || g, 'NotInformed', true, now(), false
                FROM generate_series(1, 50000) g;
                ANALYZE parties.parties;
                """, connection);
            await insert.ExecuteNonQueryAsync();
        }

        var (id, stamp) = await tenancy.SeedUserAsync(perf, "dono-perf@teste.com");
        var info = (await tenancy.Factory.Services.GetRequiredService<ITenantDirectory>().FindAsync(perf.Id))!;
        await using (var scope = await tenancy.Factory.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(info))
        {
            var db = scope.ServiceProvider.GetRequiredService<BemAqui.Identity.Application.IIdentityDb>();
            db.UserGroups.Add(new UserGroup { UserId = id, GroupId = db.Groups.Single(g => g.SeedKey == Group.OwnerSeedKey).Id });
            await db.SaveChangesAsync();
        }

        var client = tenancy.ClientFor("perf.localhost");
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokens.Create(perf.Id, "perf.localhost", userId: id, stamp: stamp));
        (await client.GetAsync("/api/v1/parties?q=metalurgica%2049")).EnsureSuccessStatusCode(); // aquece

        var watch = Stopwatch.StartNew();
        var page = await JsonAsync(await client.GetAsync("/api/v1/parties?q=metalurgica%2049"));
        watch.Stop();

        page.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
        watch.ElapsedMilliseconds.Should().BeLessThan(1000, "PRD §6: lista com 50 mil em menos de 1 s (CA 18)");
    }
}
