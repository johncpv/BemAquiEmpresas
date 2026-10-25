using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PLN-02 com o banco real: a fila com prometido, previsto, folga e risco (CA 1); entrada pela data prometida, saída e posições contíguas,
/// com o histórico automático (CA 2 e 8); carga inicial sem tocar <c>commercial.projects.priority</c> (CA 3); prévia sem gravar e igual ao
/// cálculo depois de salvar (CA 4 e 6); aviso de quem passa a atrasar (CA 5); concorrência com duas abas (CA 6); permutação inválida
/// (CA 7); histórico manual (CA 8); <see cref="IPlanningQueue"/> (CA 9); rate limit da prévia (CA 10); três camadas (CA 11). A fila é do
/// tenant inteiro: cada teste usa projetos próprios, com datas prometidas bem no futuro, e filtra pelos ids que criou.
/// </summary>
public partial class FabricationTests
{
    private const string Queue = "/api/v1/production/planning/queue";

    /// <summary>Dois projetos no mesmo centro (o roteiro e o centro do primeiro), um OS de fabricação cada, com as datas prometidas dadas.</summary>
    private async Task<(HttpClient Owner, Guid First, Guid Second, Guid FirstOrder, Guid SecondOrder)> QueuePairAsync(DateOnly firstPromised, DateOnly secondPromised)
    {
        var (owner, routing, first, orders, _) = await KanbanDatasetAsync(1);
        var product = await ProductAsync();
        var (second, _, items) = await ApprovedAsync(owner, [("Equipment", product, 1m)], routingOnItems: routing);
        await SetPromisedAsync(first, firstPromised);
        await SetPromisedAsync(second, secondPromised);
        var secondOrder = (await OrderOfAsync(items[0]))!.Id;
        return (owner, first, second, orders[0], secondOrder);
    }

    private static async Task<JsonElement> PlanningQueueAsync(HttpClient client)
    {
        var response = await client.GetAsync(Queue);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static List<Guid> QueueOrder(JsonElement queue) => [.. queue.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("projectId").GetGuid())];

    private static JsonElement Row(JsonElement queue, Guid project) =>
        queue.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("projectId").GetGuid() == project);

    /// <summary>A ordem com <paramref name="moved"/> logo antes de <paramref name="before"/>.</summary>
    private static List<Guid> MoveBefore(IEnumerable<Guid> order, Guid moved, Guid before)
    {
        var list = order.Where(id => id != moved).ToList();
        list.Insert(list.IndexOf(before), moved);
        return list;
    }

    private Task<List<PlanningQueueEntry>> StoredQueueAsync() =>
        WithDbAsync(db => db.PlanningQueue.AsNoTracking().OrderBy(e => e.Position).ToListAsync());

    private async Task<IReadOnlyList<Guid>> SyncQueueAsync()
    {
        await using var scope = await ScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<PlanningQueueStore>().SyncAsync();
    }

    [Fact]
    public async Task Fila_mostra_os_projetos_pela_data_prometida_e_a_previa_nao_grava()
    {
        var (owner, first, second, firstOrder, secondOrder) = await QueuePairAsync(Today.AddDays(3000), Today.AddDays(3001));
        await RecalcNowAsync();
        await DrainAsync();

        var queue = await PlanningQueueAsync(owner);
        var order = QueueOrder(queue);
        order.IndexOf(first).Should().BeLessThan(order.IndexOf(second), "CA 2: pela data prometida");
        var row = Row(queue, first);
        row.GetProperty("projectNumber").GetString().Should().StartWith("PRJ-");
        row.GetProperty("partyName").GetString().Should().StartWith("Cliente ");
        row.GetProperty("openOrders").GetInt32().Should().Be(1);
        row.GetProperty("remainingHours").GetDecimal().Should().Be(7m, "P 2 h + C 1,5 h + M 3 h + Q 0,5 h");
        row.GetProperty("promisedDate").GetString().Should().Be(Iso(Today.AddDays(3000)));
        row.GetProperty("hasForecast").GetBoolean().Should().BeTrue();
        row.GetProperty("risk").GetString().Should().Be("None");
        row.GetProperty("slackDays").GetInt32().Should().BeGreaterThan(0);
        queue.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("position").GetInt32()).Should().Equal(Enumerable.Range(1, order.Count), "posições contíguas");

        // CA 4: a prévia com o segundo antes do primeiro calcula sem gravar nada.
        var counts = await WithDbAsync(async db => (await db.ScheduleRuns.CountAsync(), await db.PlanningQueueChanges.CountAsync(), await db.AuditEntries.CountAsync()));
        var proposed = MoveBefore(order, second, first);
        var preview = await owner.PostAsJsonAsync(Queue + "/preview", new { order = proposed });
        preview.StatusCode.Should().Be(HttpStatusCode.OK, await preview.Content.ReadAsStringAsync());
        var rows = (await JsonAsync(preview)).GetProperty("rows").EnumerateArray().ToDictionary(r => r.GetProperty("projectId").GetGuid());
        rows[second].GetProperty("positionAfter").GetInt32().Should().Be(rows[first].GetProperty("positionAfter").GetInt32() - 1);
        var firstAfter = DateOnly.Parse(rows[first].GetProperty("forecastAfter").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var secondAfter = DateOnly.Parse(rows[second].GetProperty("forecastAfter").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        firstAfter.Should().BeOnOrAfter(DateOnly.Parse(rows[first].GetProperty("forecastBefore").GetString()!, System.Globalization.CultureInfo.InvariantCulture),
            "quem perde a frente no mesmo centro não adianta");
        (await WithDbAsync(async db => (await db.ScheduleRuns.CountAsync(), await db.PlanningQueueChanges.CountAsync(), await db.AuditEntries.CountAsync())))
            .Should().Be(counts, "CA 4: a prévia não grava nada");

        // CA 6: salvar grava a ordem, a versão, o histórico e pede o recálculo; o plano depois de salvar é a prévia.
        await ClearPendingAsync();
        var saved = await owner.PutAsJsonAsync(Queue, new { order = proposed, version = queue.GetProperty("version").GetUInt32() });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var body = await JsonAsync(saved);
        body.GetProperty("changed").GetBoolean().Should().BeTrue();
        body.GetProperty("recalculationRequested").GetBoolean().Should().BeTrue();
        QueueOrder(body.GetProperty("queue")).Should().Equal(proposed);
        body.GetProperty("queue").GetProperty("version").GetUInt32().Should().NotBe(queue.GetProperty("version").GetUInt32());
        (await PendingAsync()).Should().BeTrue("CA 6: um recálculo é pedido (PLN-01b)");
        (await RunPendingAsync()).Run!.Status.Should().Be(ScheduleRunStatus.Succeeded);
        (await PlanForecastAsync(first))!.FabricationEnd.Should().NotBeNull();
        (await PlannedEndAsync([firstOrder])).Should().Be((await PlanForecastAsync(first))!.FabricationEnd);
        (await PlanForecastAsync(second))!.ForecastDate.Should().Be(secondAfter, "a prévia é igual ao cálculo depois de salvar");
        (await PlanForecastAsync(first))!.ForecastDate.Should().Be(firstAfter);
        (await PlannedEndAsync([secondOrder])).Should().NotBeNull();

        // Auditoria (CA 6 e 8): o histórico manual com quem e as posições, e o audit_log da posição antes/depois.
        var changes = await JsonAsync(await owner.GetAsync(Queue + "/changes?pageSize=50"));
        var manual = changes.GetProperty("items").EnumerateArray().First(c => c.GetProperty("kind").GetString() == "Manual");
        manual.GetProperty("who").GetString().Should().NotBe("Sistema");
        manual.GetProperty("reason").GetString().Should().Be(PlanningQueueService.ManualReason);
        manual.GetProperty("moves").EnumerateArray().Should().Contain(m => m.GetProperty("projectId").GetGuid() == second
            && m.GetProperty("to").GetInt32() == proposed.IndexOf(second) + 1 && m.GetProperty("from").GetInt32() == order.IndexOf(second) + 1);
        var entry = (await StoredQueueAsync()).Single(e => e.ProjectId == second).Id.ToString();
        (await WithDbAsync(db => db.AuditEntries.AsNoTracking().Where(a => a.Entity == nameof(PlanningQueueEntry) && a.EntityId == entry && a.Action == "Updated").ToListAsync()))
            .Should().Contain(a => a.Changes.Contains("\"position\"", StringComparison.Ordinal));

        // CA 9: o IPlanningQueue devolve a ordem gravada.
        await using var scope = await ScopeAsync();
        (await scope.ServiceProvider.GetRequiredService<IPlanningQueue>().OrderAsync()).Should().Equal(proposed);
    }

    [Fact]
    public async Task Previa_avisa_quem_passa_a_atrasar()
    {
        // O primeiro tem a data prometida justa (a do plano de hoje); passar o segundo na frente no mesmo centro atrasa o primeiro.
        var (owner, first, second, firstOrder, _) = await QueuePairAsync(Today.AddDays(3100), Today.AddDays(3101));
        await RecalcNowAsync();
        await DrainAsync();
        var planned = (await PlanForecastAsync(first))!.ForecastDate!.Value;
        await SetPromisedAsync(first, planned);
        await SetPromisedAsync(second, Today.AddDays(3101));
        var order = QueueOrder(await PlanningQueueAsync(owner));
        if (order.IndexOf(first) > order.IndexOf(second))
        {
            order = MoveBefore(order, first, second);
        }

        var proposed = MoveBefore(order, second, first);
        var rows = (await JsonAsync(await owner.PostAsJsonAsync(Queue + "/preview", new { order = proposed }))).GetProperty("rows").EnumerateArray()
            .ToDictionary(r => r.GetProperty("projectId").GetGuid());
        var after = DateOnly.Parse(rows[first].GetProperty("forecastAfter").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        if (after > planned)
        {
            rows[first].GetProperty("worsened").GetBoolean().Should().BeTrue("CA 5: passa a atrasar");
            rows[first].GetProperty("daysLate").GetInt32().Should().Be(after.DayNumber - planned.DayNumber);
            rows[first].GetProperty("riskAfter").GetString().Should().Be("AtRisk");
        }
        else
        {
            rows[first].GetProperty("worsened").GetBoolean().Should().BeFalse();
        }

        rows[second].GetProperty("worsened").GetBoolean().Should().BeFalse("quem passa na frente não piora");
        (await PlannedEndAsync([firstOrder])).Should().NotBeNull();
    }

    [Fact]
    public async Task Duas_abas_a_segunda_leva_409_e_a_lista_errada_leva_422()
    {
        var (owner, first, second, _, _) = await QueuePairAsync(Today.AddDays(3200), Today.AddDays(3201));
        var (_, other) = await ActorAsync("production:view,edit");
        await SyncQueueAsync();
        var tabA = await PlanningQueueAsync(owner);
        var tabB = await PlanningQueueAsync(other);
        var order = QueueOrder(tabA);
        var swapped = MoveBefore(order, second, first);

        var saved = await owner.PutAsJsonAsync(Queue, new { order = swapped, version = tabA.GetProperty("version").GetUInt32() });
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
        var stale = await other.PutAsJsonAsync(Queue, new { order = order, version = tabB.GetProperty("version").GetUInt32() });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict, "CA 6: a fila é editada por duas pessoas; 409, não 500");
        var problem = await JsonAsync(stale);
        Code(problem).Should().Be("concurrency_conflict");
        problem.GetProperty("detail").GetString().Should().Be("A fila mudou desde que você abriu. Recarregue.");
        (await StoredQueueAsync()).Select(e => e.ProjectId).Should().Equal(swapped, "a segunda aba não gravou");

        // CA 7: lista que não é uma permutação exata da fila atual (a menos, a mais, repetido, de outro tenant) → 422 sem gravar.
        var version = (await PlanningQueueAsync(owner)).GetProperty("version").GetUInt32();
        var foreign = Guid.CreateVersion7();
        foreach (var wrong in new List<Guid>[] { [.. swapped.Skip(1)], [.. swapped, foreign], [.. swapped.Skip(1), swapped[1]], [.. swapped.Skip(1), foreign] })
        {
            var invalid = await owner.PutAsJsonAsync(Queue, new { order = wrong, version });
            invalid.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await JsonAsync(invalid)).GetProperty("detail").GetString().Should().Be("A fila mudou. Recarregue e reordene de novo.");
            (await owner.PostAsJsonAsync(Queue + "/preview", new { order = wrong })).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        (await StoredQueueAsync()).Select(e => e.ProjectId).Should().Equal(swapped);
        (await PlanningQueueAsync(owner)).GetProperty("version").GetUInt32().Should().Be(version, "nada foi gravado");
    }

    [Fact]
    public async Task Projeto_novo_entra_pela_data_prometida_e_o_que_terminou_sai_com_historico()
    {
        var (owner, first, second, _, secondOrder) = await QueuePairAsync(Today.AddDays(3300), Today.AddDays(3310));
        await SyncQueueAsync();

        // Um projeto novo com a data entre as duas entra logo antes do primeiro com data posterior (o segundo, aqui).
        var (_, routing, third, _, _) = await KanbanDatasetAsync(1);
        await SetPromisedAsync(third, Today.AddDays(3305));
        var stored = await SyncQueueAsync();
        var positions = PlanningQueueRules.Positions(stored);
        positions[third].Should().Be(positions[second] - 1, "CA 2: entra logo antes do primeiro com data prometida posterior");
        positions[third].Should().BeGreaterThan(positions[first]);
        (await StoredQueueAsync()).Select(e => e.Position).Should().Equal(Enumerable.Range(1, stored.Count), "sem buraco");

        // O segundo teve a OS cancelada: sai da fila, as posições continuam contíguas, e o histórico diz o motivo.
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE id = {secondOrder}"));
        var after = await SyncQueueAsync();
        after.Should().NotContain(second);
        (await StoredQueueAsync()).Select(e => e.Position).Should().Equal(Enumerable.Range(1, after.Count));

        var changes = (await JsonAsync(await owner.GetAsync(Queue + "/changes?pageSize=100"))).GetProperty("items").EnumerateArray().ToList();
        changes.Should().Contain(c => c.GetProperty("kind").GetString() == "Entered" && c.GetProperty("reason").GetString() == "Entrou pela data prometida"
            && c.GetProperty("who").GetString() == "Sistema" && c.GetProperty("moves")[0].GetProperty("projectId").GetGuid() == third);
        changes.Should().Contain(c => c.GetProperty("kind").GetString() == "Left" && c.GetProperty("reason").GetString() == "Saiu: OS canceladas"
            && c.GetProperty("moves")[0].GetProperty("projectId").GetGuid() == second && c.GetProperty("moves")[0].GetProperty("to").ValueKind == JsonValueKind.Null);
        routing.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Carga_inicial_pela_data_prometida_sem_tocar_a_prioridade_do_projeto()
    {
        var (owner, first, second, _, _) = await QueuePairAsync(Today.AddDays(3400), Today.AddDays(3401));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET priority = 1 WHERE id = {second}"));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET priority = 99 WHERE id = {first}"));

        // Tenant que nunca teve fila (a migration só cria as tabelas): a primeira manutenção monta a fila inteira.
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"DELETE FROM production.planning_queue"));
        var order = (await SyncQueueAsync()).ToList();
        order.IndexOf(first).Should().BeLessThan(order.IndexOf(second), "CA 3: pela data prometida, nunca pela priority");
        (await WithDbAsync(db => db.Projects.AsNoTracking().Where(p => p.Id == first || p.Id == second).Select(p => new { p.Id, p.Priority }).ToListAsync()))
            .Should().BeEquivalentTo([new { Id = first, Priority = (int?)99 }, new { Id = second, Priority = (int?)1 }], "a coluna fica sem uso, sem ler nem gravar");
        var initial = (await JsonAsync(await owner.GetAsync(Queue + "/changes"))).GetProperty("items")[0];
        initial.GetProperty("kind").GetString().Should().Be("Initial");
        initial.GetProperty("reason").GetString().Should().Be(PlanningQueueStore.InitialReason);
        initial.GetProperty("moves").GetArrayLength().Should().Be(order.Count);
    }

    [Fact]
    public async Task Fila_tem_as_tres_camadas_e_o_rate_limit_da_previa()
    {
        var (owner, _, _, _, _) = await QueuePairAsync(Today.AddDays(3500), Today.AddDays(3501));
        var (_, editor) = await ActorAsync("production:view,edit");
        var (_, viewer) = await ActorAsync("production:view");
        var (_, nobody) = await ActorAsync(null);
        var queue = await PlanningQueueAsync(viewer);
        var order = QueueOrder(queue);
        var version = queue.GetProperty("version").GetUInt32();

        var denied = await viewer.PutAsJsonAsync(Queue, new { order, version });
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden, "CA 11: salvar sem production.edit");
        Code(await JsonAsync(denied)).Should().Be("forbidden");
        (await viewer.PostAsJsonAsync(Queue + "/preview", new { order })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.GetAsync(Queue + "/changes")).StatusCode.Should().Be(HttpStatusCode.OK);
        var none = await nobody.GetAsync(Queue);
        none.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(none)).Should().Be("forbidden");
        (await (await owner.GetAsync(Queue)).Content.ReadAsStringAsync()).Should().NotContainAny(["cost", "Cost", "amount"], "nenhuma resposta tem custo");

        await SetProductionReleasedAsync(false);
        try
        {
            foreach (var blocked in new[] { await editor.GetAsync(Queue), await editor.PutAsJsonAsync(Queue, new { order, version }), await editor.PostAsJsonAsync(Queue + "/preview", new { order }) })
            {
                blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
                Code(await JsonAsync(blocked)).Should().Be("module_not_released");
            }
        }
        finally
        {
            await SetProductionReleasedAsync(true);
        }

        // CA 10: 20 prévias por minuto por usuário; a 21ª responde 429 com a mensagem da política planning-simulate.
        HttpResponseMessage? limited = null;
        for (var i = 0; i < 25 && limited is null; i++)
        {
            var response = await editor.PostAsJsonAsync(Queue + "/preview", new { order });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
            else
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }

        limited.Should().NotBeNull("acima de 20 por minuto");
        (await JsonAsync(limited!)).GetProperty("title").GetString().Should().Be("Aguarde alguns segundos para simular de novo.");
        (await owner.PostAsJsonAsync(Queue + "/preview", new { order })).StatusCode.Should().Be(HttpStatusCode.OK, "o limite é por usuário");
    }
}
