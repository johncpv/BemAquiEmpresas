using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Commercial.Contracts;
using BemAqui.Identity.Domain;
using BemAqui.Inventory.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Commercial;

public partial class StageTemplateTests
{
    [Fact]
    public async Task Cria_edita_sobe_a_revisao_audita_e_le_pelo_contrato_sem_custo()
    {
        var (userId, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        var position = await PositionAsync();
        var created = await CreateAsync(owner, Unique("Implantação"), steps: Steps(position));
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("revision").GetInt32().Should().Be(1);
        created.GetProperty("totalHoursPerUnit").GetDecimal().Should().Be(5.5m);
        created.GetProperty("steps")[0].GetProperty("positionName").GetString().Should().StartWith("Consultor");
        created.GetProperty("suggestedMilestones")[1].GetProperty("trigger").GetString().Should().Be("StageCompleted");

        // CA 4: o lookup de cargos traz só id e nome, nunca custo.
        var positions = await (await owner.GetAsync(Base + "/lookups/positions")).Content.ReadAsStringAsync();
        positions.Should().Contain(position.ToString()).And.NotContainAny("hourlyCost", "HourlyCost", "cost");

        // CA 2 e 3: editar sobe a revisão; a etapa com id continua a mesma.
        var stepId = created.GetProperty("steps")[0].GetProperty("id").GetGuid();
        var updated = await owner.PutAsJsonAsync($"{Base}/{id}", new
        {
            name = created.GetProperty("name").GetString(), segment = (string?)null,
            steps = new[] { Step("K", "Execution", 6m, position, stepId, "Kickoff"), Step("L", "Execution", 2m), Step("G", "Delivery", 1m), Step("A", "Acceptance", 0m) },
            suggestedMilestones = Milestones, version = Version(created),
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var detail = await JsonAsync(updated);
        detail.GetProperty("revision").GetInt32().Should().Be(2);
        detail.GetProperty("steps")[0].GetProperty("id").GetGuid().Should().Be(stepId);

        // Versão velha: 409.
        var stale = await owner.PutAsJsonAsync($"{Base}/{id}", new { name = "X", segment = (string?)null, steps = Steps(), suggestedMilestones = Milestones, version = Version(created) });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // CA 13: o contrato devolve revisão, etapas e marcos.
        await using (var scope = await TenantScopeAsync())
        {
            var info = await scope.ServiceProvider.GetRequiredService<IStageTemplates>().GetAsync(id);
            info!.Revision.Should().Be(2);
            info.Steps.Select(s => s.Code).Should().Equal("K", "L", "G", "A");
            info.Steps[0].PositionId.Should().Be(position);
            info.SuggestedMilestones.Should().HaveCount(2);
        }

        // CA 14: a auditoria mostra quem mudou o modelo e as etapas.
        var history = await JsonAsync(await owner.GetAsync($"{Base}/{id}/history"));
        var entries = history.GetProperty("items").EnumerateArray().ToList();
        entries.Should().Contain(e => e.GetProperty("entity").GetString() == "StageTemplate" && e.GetProperty("action").GetString() == "Updated");
        entries.Should().Contain(e => e.GetProperty("entity").GetString() == "StageTemplateStep" && e.GetProperty("action").GetString() == "Created");
        entries.All(e => e.GetProperty("userId").GetGuid() == userId).Should().BeTrue();
    }

    [Fact]
    public async Task Servidor_revalida_e_responde_422_na_linha()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        var response = await owner.PostAsJsonAsync(Base, new
        {
            name = Unique("Inválido"), segment = (string?)null,
            steps = new[] { Step("A", "Execution"), Step("B", "Delivery"), Step("C", "Execution"), Step("C", "Acceptance", -1m) },
            suggestedMilestones = new[] { new { percent = 60m, trigger = "StageCompleted", stageCode = "B", daysAfterEvent = 0 } },
        });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var errors = (await JsonAsync(response)).GetProperty("errors");
        errors.TryGetProperty("steps[2].role", out _).Should().BeTrue("Execução depois da Entrega");
        errors.TryGetProperty("steps[3].code", out _).Should().BeTrue("letra repetida");
        errors.TryGetProperty("steps[3].standardHoursPerUnit", out _).Should().BeTrue();
        errors.TryGetProperty("suggestedMilestones[0].stageCode", out _).Should().BeTrue("a Entrega usa o gatilho próprio");
        errors.TryGetProperty("suggestedMilestones", out _).Should().BeTrue("soma diferente de 100%");

        // CA 3: cargo inexistente ou inativo é recusado na linha.
        var inactive = await PositionAsync(active: false);
        var refused = await owner.PostAsJsonAsync(Base, new { name = Unique("Cargo"), segment = (string?)null, steps = Steps(inactive), suggestedMilestones = Milestones });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(refused)).GetProperty("errors").TryGetProperty("steps[0].positionId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Cargo_inativado_continua_gravado_com_o_selo_e_a_edicao_o_aceita()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        var position = await PositionAsync();
        var created = await CreateAsync(owner, Unique("Cargo inativo"), steps: Steps(position));
        await WithDbAsync(async db =>
        {
            var p = await db.JobPositions.SingleAsync(x => x.Id == position);
            p.IsActive = false;
            return await db.SaveChangesAsync();
        });

        var id = created.GetProperty("id").GetGuid();
        var detail = await JsonAsync(await owner.GetAsync($"{Base}/{id}"));
        detail.GetProperty("steps")[0].GetProperty("positionActive").GetBoolean().Should().BeFalse();
        var steps = detail.GetProperty("steps").EnumerateArray()
            .Select(s => Step(s.GetProperty("code").GetString()!, s.GetProperty("role").GetString()!, s.GetProperty("standardHoursPerUnit").GetDecimal(),
                s.GetProperty("positionId").ValueKind == JsonValueKind.Null ? null : s.GetProperty("positionId").GetGuid(), s.GetProperty("id").GetGuid()))
            .ToArray();
        var saved = await owner.PutAsJsonAsync($"{Base}/{id}", new { name = detail.GetProperty("name").GetString(), segment = (string?)null, steps, suggestedMilestones = Milestones, version = Version(detail) });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Vinculo_so_com_item_de_servico_ativo_e_um_modelo_por_item()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        await ServicesAsync(true);
        var first = (await CreateAsync(owner, Unique("Vínculo A"))).GetProperty("id").GetGuid();
        var second = (await CreateAsync(owner, Unique("Vínculo B"))).GetProperty("id").GetGuid();
        var component = await ItemAsync(ItemType.Component);
        var service = await ItemAsync(ItemType.Service);

        var refused = await owner.PostAsJsonAsync($"{Base}/{first}/items", new { itemId = component });
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(refused)).GetProperty("errors").GetProperty("itemId")[0].GetString().Should().Be("Só itens de serviço podem ter modelo de etapas.");

        (await owner.PostAsJsonAsync($"{Base}/{first}/items", new { itemId = service })).StatusCode.Should().Be(HttpStatusCode.Created);
        var taken = await owner.PostAsJsonAsync($"{Base}/{second}/items", new { itemId = service });
        taken.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await JsonAsync(taken)).Should().Be("stage_template_item_taken");

        var onItem = await JsonAsync(await owner.GetAsync($"{Base}/by-item/{service}"));
        onItem.GetProperty("stageTemplateId").GetGuid().Should().Be(first);

        // CA 10: vincular exige projects.edit; quem só vê recebe 403.
        var viewer = await ViewerAsync();
        (await viewer.PostAsJsonAsync($"{Base}/{second}/items", new { itemId = await ItemAsync(ItemType.Service) })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.GetAsync($"{Base}/by-item/{service}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
