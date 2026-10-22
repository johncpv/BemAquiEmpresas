using System.Net;
using System.Net.Http.Json;
using BemAqui.Engineering.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Engineering;

public partial class RoutingTests
{
    [Fact]
    public async Task Cria_roteiro_ativo_com_sequencia_centro_validado_checklist_e_auditoria()
    {
        var engineer = await EngineerAsync();
        var (template, cut, _) = await ThreeStepsAsync(engineer);

        template.GetProperty("isActive").GetBoolean().Should().BeTrue("o roteiro nasce ativo (CA 2)");
        var steps = template.GetProperty("steps");
        steps.EnumerateArray().Select(s => s.GetProperty("sequence").GetInt32()).Should().Equal(1, 2, 3);
        steps.EnumerateArray().Select(s => s.GetProperty("code").GetString()).Should().Equal("C", "M", "Q");
        steps[0].GetProperty("workCenterId").GetGuid().Should().Be(cut);
        steps[0].GetProperty("workCenterName").GetString().Should().StartWith("Corte");
        steps[1].GetProperty("checklist")[1].GetProperty("text").GetString().Should().Be("Fixar guias");
        steps[1].GetProperty("checklist")[1].GetProperty("sequence").GetInt32().Should().Be(2);
        steps[2].GetProperty("isQualityStep").GetBoolean().Should().BeTrue();
        steps[2].GetProperty("requiresApproval").GetBoolean().Should().BeTrue();
        template.GetProperty("totalStandardHours").GetDecimal().Should().Be(5.75m);

        var id = template.GetProperty("id").GetGuid();
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        (await db.AuditEntries.AnyAsync(a => a.Entity == "RoutingTemplate" && a.EntityId == id.ToString())).Should().BeTrue("auditado (CA 18)");
        var list = await JsonAsync(await engineer.GetAsync($"{Base}?q={Uri.EscapeDataString(template.GetProperty("name").GetString()!)}"));
        list.GetProperty("items")[0].GetProperty("codes").EnumerateArray().Select(c => c.GetString()).Should().Equal("C", "M", "Q");
        (await engineer.GetFromJsonAsync<string[]>($"{Base}/product-types")).Should().Contain("Elevador", "as sugestões vêm dos tipos já usados (CA 2)");
        (await engineer.GetFromJsonAsync<SuggestedCenter[]>($"{Base}/suggested-work-centers")).Should().Contain(s => s.StepName == "Corte" && s.WorkCenterId == cut);
    }

    private sealed record SuggestedCenter(string StepName, Guid WorkCenterId);

    [Fact]
    public async Task Nome_repetido_sem_diferenciar_maiusculas_e_validacoes_por_linha_dao_422_no_campo()
    {
        var engineer = await EngineerAsync();
        var center = await CenterAsync("Solda");
        var inactive = await CenterAsync("Antiga", active: false);
        var name = Unique("Porta");
        await CreatedAsync(await engineer.PostAsJsonAsync(Base, Template(name, Step("Solda", "S", center))));

        var twice = await JsonAsync(await engineer.PostAsJsonAsync(Base, Template(name.ToUpperInvariant(), Step("Solda", "S", center))));
        FieldError(twice, "name").Should().Be("Já existe um roteiro com este nome.");

        var empty = await engineer.PostAsJsonAsync(Base, Template(Unique("Vazio")));
        empty.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        FieldError(await JsonAsync(empty), "steps").Should().Be("Inclua ao menos uma etapa.");

        var problem = await JsonAsync(await engineer.PostAsJsonAsync(Base, Template(Unique("Erros"),
            Step("Qualidade", "Q", center, quality: true), Step("Corte", "C", inactive), Step("Cabine", "c", Guid.CreateVersion7()), Step("", "1", null))));
        FieldError(problem, "steps[0].isQualityStep").Should().Be("A primeira etapa não pode ser de qualidade.");
        FieldError(problem, "steps[1].workCenterId").Should().Be("Escolha um centro de trabalho ativo.");
        FieldError(problem, "steps[2].code").Should().Be("A letra C já está na etapa 2.");
        FieldError(problem, "steps[2].workCenterId").Should().Be("Escolha um centro de trabalho ativo.", "centro inexistente (IWorkCenters.FindAsync)");
        FieldError(problem, "steps[3].name").Should().Be("Informe o nome da etapa, com até 60 caracteres.");
        FieldError(problem, "steps[3].code").Should().Be("Informe a letra da etapa, de A a Z.");
        FieldError(problem, "steps[3].workCenterId").Should().Be("Escolha o centro de trabalho da etapa.");

        var twoQuality = await JsonAsync(await engineer.PostAsJsonAsync(Base, Template(Unique("Q2"), Step("Corte", "C", center), Step("Inspeção", "I", center, quality: true), Step("Qualidade", "Q", center, quality: true))));
        FieldError(twoQuality, "steps[2].isQualityStep").Should().Be("Duas etapas de qualidade não podem ficar seguidas.");

        var tooMany = Enumerable.Range(0, 31).Select(i => Step("Etapa " + i, ((char)('A' + (i % 26))).ToString(), center)).ToArray();
        FieldError(await JsonAsync(await engineer.PostAsJsonAsync(Base, Template(Unique("Longo"), tooMany))), "steps").Should().Be("Inclua no máximo 30 etapas.");
    }

    [Fact]
    public async Task Editar_reordena_sem_trocar_ids_troca_letras_remove_etapa_e_versao_antiga_da_409()
    {
        var engineer = await EngineerAsync();
        var (template, cut, assembly) = await ThreeStepsAsync(engineer);
        var id = template.GetProperty("id").GetGuid();
        var version = template.GetProperty("version").GetUInt32();
        var steps = template.GetProperty("steps");
        var (first, second, third) = (steps[0].GetProperty("id").GetGuid(), steps[1].GetProperty("id").GetGuid(), steps[2].GetProperty("id").GetGuid());

        // Montagem sobe para 1ª e as letras C e M trocam de lugar num salvar só (constraint adiável); a Qualidade sai.
        var update = new
        {
            name = template.GetProperty("name").GetString(), productType = "Elevador", version,
            steps = new[] { Step("Montagem", "C", assembly, 4m, id: second), Step("Corte", "M", cut, 2m, id: first), Step("Embalagem", "E", assembly, 1m) },
        };
        var saved = await engineer.PutAsJsonAsync($"{Base}/{id}", update);
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var after = await JsonAsync(saved);
        after.GetProperty("steps").EnumerateArray().Select(s => (s.GetProperty("sequence").GetInt32(), s.GetProperty("name").GetString(), s.GetProperty("code").GetString()))
            .Should().Equal((1, "Montagem", "C"), (2, "Corte", "M"), (3, "Embalagem", "E"));
        after.GetProperty("steps")[0].GetProperty("id").GetGuid().Should().Be(second, "reordenar não troca ids (Regras)");
        after.GetProperty("version").GetUInt32().Should().NotBe(version, "a versão sobe mesmo quando só as etapas mudam");

        var stale = await engineer.PutAsJsonAsync($"{Base}/{id}", update);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(stale);
        Code(problem).Should().Be("concurrency_conflict");
        problem.GetProperty("detail").GetString().Should().Be("O roteiro mudou desde que você abriu. Recarregue.");

        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        (await db.RoutingTemplateSteps.AnyAsync(s => s.Id == third)).Should().BeFalse("a etapa que saiu da lista é removida");

        var history = await JsonAsync(await engineer.GetAsync($"{Base}/{id}/history?pageSize=100"));
        var entities = history.GetProperty("items").EnumerateArray().Select(e => (e.GetProperty("entity").GetString(), e.GetProperty("action").GetString())).ToList();
        entities.Should().Contain(("RoutingTemplate", "Created")).And.Contain(("RoutingTemplateStep", "Updated")).And.Contain(("RoutingTemplateStep", "Deleted"), "o histórico mostra as etapas, inclusive a removida (CA 18)");
    }

    [Fact]
    public async Task Duplicar_traz_etapas_letras_e_checklist_sem_produtos()
    {
        var engineer = await EngineerAsync();
        var (template, _, _) = await ThreeStepsAsync(engineer);
        var id = template.GetProperty("id").GetGuid();
        var (item, _) = await ItemAsync();
        await CreatedAsync(await engineer.PostAsJsonAsync($"{Base}/{id}/items", new { itemId = item }));

        var copy = await JsonAsync(await engineer.GetAsync($"{Base}/{id}/duplicate"));
        copy.GetProperty("name").GetString().Should().Be(template.GetProperty("name").GetString() + " (cópia)");
        copy.GetProperty("steps").EnumerateArray().Select(s => s.GetProperty("code").GetString()).Should().Equal("C", "M", "Q");
        copy.GetProperty("steps")[1].GetProperty("checklist").EnumerateArray().Select(c => c.GetString()).Should().Equal("Conferir prumo", "Fixar guias");
        copy.GetProperty("steps").EnumerateArray().Should().OnlyContain(s => s.GetProperty("id").ValueKind == System.Text.Json.JsonValueKind.Null);

        var created = await CreatedAsync(await engineer.PostAsJsonAsync(Base, copy));
        created.GetProperty("itemCount").GetInt32().Should().Be(0, "cada produto tem um roteiro só (CA 9)");
    }

    [Fact]
    public async Task Contrato_IRoutingTemplates_devolve_etapas_horas_por_centro_e_ignora_inativo()
    {
        var engineer = await EngineerAsync();
        var (template, cut, assembly) = await ThreeStepsAsync(engineer);
        var id = template.GetProperty("id").GetGuid();
        var (item, _) = await ItemAsync(BemAqui.Inventory.Contracts.ItemType.Component);
        await CreatedAsync(await engineer.PostAsJsonAsync($"{Base}/{id}/items", new { itemId = item }));

        await using (var scope = await ScopeAsync())
        {
            var routings = scope.ServiceProvider.GetRequiredService<IRoutingTemplates>();
            (await routings.StepsAsync(id)).Select(s => s.Code).Should().Equal("C", "M", "Q");
            (await routings.ForItemAsync(item))!.Id.Should().Be(id);
            (await routings.TotalStandardHoursAsync(id)).Should().BeEquivalentTo([new WorkCenterStandardHours(cut, 2m), new WorkCenterStandardHours(assembly, 3.75m)]);
            (await routings.ActiveAsync()).Should().Contain(t => t.Id == id);
        }

        (await engineer.PostAsync($"{Base}/{id}/inactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using (var scope = await ScopeAsync())
        {
            var routings = scope.ServiceProvider.GetRequiredService<IRoutingTemplates>();
            (await routings.ActiveAsync()).Should().NotContain(t => t.Id == id, "inativo some dos seletores (CA 11)");
            (await routings.ForItemAsync(item)).Should().BeNull("e da sugestão pelo produto");
            (await routings.FindAsync([id])).Should().ContainSingle(t => !t.IsActive, "e continua resolvido por id para quem já usa");
        }

        (await engineer.PostAsync($"{Base}/{id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}