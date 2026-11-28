using System.Net;
using System.Net.Http.Json;
using BemAqui.Commercial.Application;
using BemAqui.Host.Persistence.Migrations;
using BemAqui.Identity.Domain;
using BemAqui.Inventory.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace BemAqui.Integration.Tests.Commercial;

public partial class StageTemplateTests
{
    private Task<int> DefaultsAsync(string? segment) =>
        WithDbAsync(db => db.StageTemplates.CountAsync(t => t.IsDefault && t.Segment == segment));

    [Fact]
    public async Task Usar_como_padrao_troca_o_padrao_e_dois_pedidos_simultaneos_dao_um_padrao_e_um_409()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        var first = await CreateAsync(owner, Unique("Oficina 1"), "workshop");
        var second = await CreateAsync(owner, Unique("Oficina 2"), "workshop");
        var firstId = first.GetProperty("id").GetGuid();

        var made = await owner.PostAsJsonAsync($"{Base}/{firstId}/default", new { version = Version(first) });
        made.StatusCode.Should().Be(HttpStatusCode.OK, await made.Content.ReadAsStringAsync());
        (await JsonAsync(made)).GetProperty("isDefault").GetBoolean().Should().BeTrue();
        var secondId = second.GetProperty("id").GetGuid();
        (await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{secondId}/default", new { version = Version(second) }))).GetProperty("isDefault").GetBoolean().Should().BeTrue();
        (await JsonAsync(await owner.GetAsync($"{Base}/{firstId}"))).GetProperty("isDefault").GetBoolean().Should().BeFalse("o anterior deixa de ser o padrão");
        (await DefaultsAsync("workshop")).Should().Be(1);

        // CA 7: dois "Usar como padrão" simultâneos no mesmo modelo, com a mesma versão: um vira padrão, o outro recebe 409.
        var third = await CreateAsync(owner, Unique("Oficina 3"), "workshop");
        var thirdId = third.GetProperty("id").GetGuid();
        var race = await Task.WhenAll(
            owner.PostAsJsonAsync($"{Base}/{thirdId}/default", new { version = Version(third) }),
            owner.PostAsJsonAsync($"{Base}/{thirdId}/default", new { version = Version(third) }));
        race.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        Code(await JsonAsync(race.Single(r => r.StatusCode == HttpStatusCode.Conflict))).Should().Be("concurrency_conflict");

        // Em modelos diferentes ao mesmo tempo: o índice único garante um padrão só (o perdedor, se houver corrida, recebe 409).
        var a = await CreateAsync(owner, Unique("Oficina 4"), "workshop");
        var b = await CreateAsync(owner, Unique("Oficina 5"), "workshop");
        var both = await Task.WhenAll(
            owner.PostAsJsonAsync($"{Base}/{a.GetProperty("id").GetGuid()}/default", new { version = Version(a) }),
            owner.PostAsJsonAsync($"{Base}/{b.GetProperty("id").GetGuid()}/default", new { version = Version(b) }));
        both.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        both.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);
        (await DefaultsAsync("workshop")).Should().Be(1);
    }

    [Fact]
    public async Task Exclusao_logica_remove_os_vinculos_recusa_o_padrao_e_libera_o_nome()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        var name = Unique("Excluir");
        var template = await CreateAsync(owner, name, "workshop");
        var id = template.GetProperty("id").GetGuid();
        (await owner.PostAsJsonAsync($"{Base}/{id}/items", new { itemId = await ItemAsync(ItemType.Service) })).StatusCode.Should().Be(HttpStatusCode.Created);

        // CA 8: o padrão não pode ser excluído nem inativado.
        var made = await JsonAsync(await owner.PostAsJsonAsync($"{Base}/{id}/default", new { version = Version(template) }));
        var refused = await owner.DeleteAsync($"{Base}/{id}?version={Version(made)}");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(refused)).GetProperty("detail").GetString().Should().Be("Escolha outro modelo padrão antes de excluir este.");

        var other = await CreateAsync(owner, Unique("Outro padrão"), "workshop");
        (await owner.PostAsJsonAsync($"{Base}/{other.GetProperty("id").GetGuid()}/default", new { version = Version(other) })).StatusCode.Should().Be(HttpStatusCode.OK);
        var current = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));

        // Sem projects.delete: 403.
        var viewer = await ViewerAsync();
        (await viewer.DeleteAsync($"{Base}/{id}?version={Version(current)}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await owner.DeleteAsync($"{Base}/{id}?version={Version(current)}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetAsync($"{Base}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var (deleted, links) = await WithDbAsync(async db => (await db.StageTemplates.IgnoreQueryFilters().SingleAsync(t => t.Id == id), await db.StageTemplateItems.CountAsync(i => i.TemplateId == id)));
        deleted.IsDeleted.Should().BeTrue();
        links.Should().Be(0, "os vínculos saem na mesma transação");

        // CA 9: o índice de nome ignora os excluídos.
        await CreateAsync(owner, name, "workshop");
    }

    [Fact]
    public async Task Pacote_do_perfil_cria_os_quatro_modelos_so_toma_o_padrao_vago_e_nao_ressuscita_o_excluido()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        var keys = StageTemplateService.Seeds.Select(s => s.Key).ToList();

        // Sem padrão do tenant: a "Implantação de software" vira o padrão.
        (await DefaultsAsync(null)).Should().Be(0);
        await SeedProfilesAsync();
        var seeded = await WithDbAsync(db => db.StageTemplates.Where(t => t.SeedKey != null).ToListAsync());
        seeded.Select(t => t.SeedKey).Should().BeEquivalentTo(keys);
        seeded.Single(t => t.IsDefault).SeedKey.Should().Be(StageTemplateService.SoftwareSeedKey);
        var consulting = seeded.Single(t => t.SeedKey == "service-consulting");
        consulting.SuggestedMilestones.Select(m => m.Percent).Should().Equal(30m, 30m, 40m);
        consulting.SuggestedMilestones[1].StageCode.Should().Be("P");
        var list = await JsonAsync(await owner.GetAsync(Base + "?situation=active&pageSize=100"));
        list.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("isSuggested").GetBoolean()).Should().HaveCount(4);

        // Idempotente: rodar de novo não cria nada.
        await SeedProfilesAsync();
        (await WithDbAsync(db => db.StageTemplates.IgnoreQueryFilters().CountAsync(t => t.SeedKey != null))).Should().Be(4);

        // O tenant edita um, troca o padrão e exclui outro: o pacote não sobrescreve, não toma o padrão e não ressuscita o excluído.
        var detail = await JsonAsync(await owner.GetAsync($"{Base}/{consulting.Id}"));
        var steps = detail.GetProperty("steps").EnumerateArray().Select(s => Step(s.GetProperty("code").GetString()!, s.GetProperty("role").GetString()!, 1m, null, s.GetProperty("id").GetGuid())).ToArray();
        (await owner.PutAsJsonAsync($"{Base}/{consulting.Id}", new { name = "Consultoria do meu jeito", segment = (string?)null, steps, suggestedMilestones = Array.Empty<object>(), version = Version(detail) }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var mine = await CreateAsync(owner, Unique("Meu padrão"));
        (await owner.PostAsJsonAsync($"{Base}/{mine.GetProperty("id").GetGuid()}/default", new { version = Version(mine) })).StatusCode.Should().Be(HttpStatusCode.OK);
        var installation = seeded.Single(t => t.SeedKey == "service-installation");
        var install = await JsonAsync(await owner.GetAsync($"{Base}/{installation.Id}"));
        (await owner.DeleteAsync($"{Base}/{installation.Id}?version={Version(install)}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await SeedProfilesAsync();
        var suggested = await JsonAsync(await owner.PostAsync(Base + "/suggested", null));
        suggested.GetProperty("created").GetInt32().Should().Be(0, "o excluído não volta nem pelo botão");
        var after = await WithDbAsync(db => db.StageTemplates.IgnoreQueryFilters().Where(t => t.SeedKey != null).ToListAsync());
        after.Should().HaveCount(4);
        after.Single(t => t.SeedKey == "service-installation").IsDeleted.Should().BeTrue("a seed_key fica no excluído");
        after.Single(t => t.SeedKey == "service-consulting").Name.Should().Be("Consultoria do meu jeito");
        after.Single(t => t.SeedKey == StageTemplateService.SoftwareSeedKey).IsDefault.Should().BeFalse();
        (await DefaultsAsync(null)).Should().Be(1);
    }

    [Fact]
    public async Task Konebras_sem_o_perfil_servicos_recebe_403_nao_ganha_modelo_e_a_migration_so_cria()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(false);
        try
        {
            var before = await WithDbAsync(db => db.StageTemplates.IgnoreQueryFilters().CountAsync());
            foreach (var response in new[] { await owner.GetAsync(Base), await owner.GetAsync(Base + "/lookups/positions"), await owner.PostAsync(Base + "/suggested", null) })
            {
                response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                Code(await JsonAsync(response)).Should().Be("module_not_released");
            }

            await SeedProfilesAsync();
            (await WithDbAsync(db => db.StageTemplates.IgnoreQueryFilters().CountAsync())).Should().Be(before, "a fábrica não tem pacote");
            (await owner.GetAsync("/api/v1/projects")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await ServicesAsync(true);
        }

        // CA 12: a migration só cria tabelas, índices e constraints; nenhum UPDATE, DELETE ou INSERT em linha existente.
        var operations = new StageTemplatesExe02().UpOperations;
        operations.Should().NotBeEmpty();
        foreach (var operation in operations)
        {
            switch (operation)
            {
                case CreateTableOperation table:
                    table.Name.Should().StartWith("stage_template");
                    break;
                case CreateIndexOperation index:
                    index.Table.Should().StartWith("stage_template");
                    break;
                case SqlOperation sql:
                    sql.Sql.Should().MatchRegex(@"^(CREATE UNIQUE INDEX \w+ ON commercial\.stage_templates |ALTER TABLE commercial\.stage_template_steps ADD CONSTRAINT )");
                    break;
                default:
                    throw new InvalidOperationException("Operação inesperada na migration: " + operation.GetType().Name);
            }
        }
    }
}
