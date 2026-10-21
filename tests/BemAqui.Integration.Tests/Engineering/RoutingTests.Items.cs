using System.Net;
using System.Net.Http.Json;
using BemAqui.Inventory.Contracts;
using BemAqui.Production.Contracts;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Engineering;

public partial class RoutingTests
{
    [Fact]
    public async Task Produto_tem_um_roteiro_so_e_o_segundo_vinculo_da_422_com_o_roteiro()
    {
        var engineer = await EngineerAsync();
        var (first, _, _) = await ThreeStepsAsync(engineer);
        var (second, _, _) = await ThreeStepsAsync(engineer);
        var (item, code) = await ItemAsync();
        var firstId = first.GetProperty("id").GetGuid();

        var link = await CreatedAsync(await engineer.PostAsJsonAsync($"{Base}/{firstId}/items", new { itemId = item }));
        link.GetProperty("code").GetString().Should().Be(code);
        var taken = await engineer.PostAsJsonAsync($"{Base}/{second.GetProperty("id").GetGuid()}/items", new { itemId = item });
        taken.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(taken);
        problem.GetProperty("detail").GetString().Should().Be($"O produto {code} já usa o roteiro {first.GetProperty("name").GetString()}.");
        problem.GetProperty("routingTemplateId").GetGuid().Should().Be(firstId, "o front oferece \"Abrir roteiro\" (CA 7)");

        var (raw, _) = await ItemAsync(ItemType.RawMaterial);
        FieldError(await JsonAsync(await engineer.PostAsJsonAsync($"{Base}/{firstId}/items", new { itemId = raw })), "itemId").Should().Be("Escolha um produto acabado ou componente ativo.");
        var (inactive, _) = await ItemAsync(active: false);
        (await engineer.PostAsJsonAsync($"{Base}/{firstId}/items", new { itemId = inactive })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var options = await JsonAsync(await engineer.GetAsync($"{Base}/products?q={code}"));
        options[0].GetProperty("routingTemplateId").GetGuid().Should().Be(firstId, "o seletor diz qual roteiro o produto já usa");
        var listed = await JsonAsync(await engineer.GetAsync($"{Base}/{firstId}/items"));
        listed.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task Excluir_sem_uso_apaga_vinculos_e_com_uso_da_409_para_inativar()
    {
        var engineer = await EngineerAsync();
        var (template, _, _) = await ThreeStepsAsync(engineer);
        var id = template.GetProperty("id").GetGuid();
        var (item, _) = await ItemAsync();
        await CreatedAsync(await engineer.PostAsJsonAsync($"{Base}/{id}/items", new { itemId = item }));
        var supervisor = await ActorAsync("production_supervision");
        (await supervisor.DeleteAsync($"{Base}/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "excluir pede engineering.delete (CA 1, 10)");

        InUse[id] = 3;
        var refused = await engineer.DeleteAsync($"{Base}/{id}");
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(refused);
        Code(problem).Should().Be("routing_template_in_use");
        problem.GetProperty("detail").GetString().Should().Be("O roteiro é usado em 3 OS. Inative-o.");
        (await JsonAsync(await engineer.GetAsync($"{Base}/{id}"))).GetProperty("usage").GetString().Should().Be("3 OS", "a lista mostra \"Usado em {n} OS\" (CA 12)");

        InUse.TryRemove(id, out _);
        (await engineer.DeleteAsync($"{Base}/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await engineer.GetAsync($"{Base}/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await using var scope = await ScopeAsync();
        var db = scope.ServiceProvider.GetRequiredService<BemAqui.Host.Persistence.TenantDbContext>();
        (await db.RoutingTemplateItems.AnyAsync(i => i.TemplateId == id)).Should().BeFalse("os vínculos de produto são apagados (CA 10)");
        (await db.RoutingTemplates.IgnoreQueryFilters().SingleAsync(t => t.Id == id)).IsDeleted.Should().BeTrue("exclusão lógica");
        (await db.AuditEntries.AnyAsync(a => a.Entity == "RoutingTemplateItem" && a.Action == "Deleted")).Should().BeTrue();
    }

    [Fact]
    public async Task Centro_usado_em_roteiro_fica_em_uso_para_a_Producao()
    {
        var engineer = await EngineerAsync();
        var (_, cut, _) = await ThreeStepsAsync(engineer);
        await using var scope = await ScopeAsync();
        var usages = scope.ServiceProvider.GetServices<IWorkCenterUsageProvider>();
        var found = new List<string?>();
        foreach (var usage in usages)
        {
            found.Add(await usage.UsageAsync(cut));
        }

        found.Should().Contain("roteiros");
    }
}