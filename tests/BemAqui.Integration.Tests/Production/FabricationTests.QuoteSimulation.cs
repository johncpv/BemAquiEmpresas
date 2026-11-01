using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.Commercial.Application;
using BemAqui.Commercial.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Production.Application;
using BemAqui.SharedKernel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

public abstract partial class FabricationTestBase
{
    protected static string SimulationUrl(Guid quote) => $"{Quotes}/{quote}/schedule-simulation";

    protected static DateOnly? DateOf(JsonElement body, string property) =>
        body.GetProperty(property).ValueKind == JsonValueKind.Null ? null : DateOnly.Parse(body.GetProperty(property).GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    protected static async Task<JsonElement> SimulateAsync(HttpClient client, Guid quote, int? position = null)
    {
        var response = await client.PostAsJsonAsync(SimulationUrl(quote), new { queuePosition = position });
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        return JsonSerializer.Deserialize<JsonElement>(body);
    }

    /// <summary>
    /// Orçamento em rascunho pelo endpoint, com os itens (tipo, produto, quantidade) e, direto na composição (a tela de custo tem os próprios
    /// testes), as horas por centro de cada item e, se pedido, uma linha de instalação.
    /// </summary>
    protected async Task<Guid> SimulationQuoteAsync(HttpClient owner, int? deliveryDays, bool installation, params (string Kind, decimal Quantity, (Guid Center, decimal Hours)[] Labor)[] lines)
    {
        var customer = await CustomerAsync(owner);
        var quote = await JsonAsync(await owner.SendAsync(Post(Quotes, new { partyId = customer, contactId = (Guid?)null, siteAddressId = (Guid?)null })));
        var id = quote.GetProperty("id").GetGuid();
        foreach (var (kind, quantity, _) in lines)
        {
            var added = await owner.PostAsJsonAsync($"{Quotes}/{id}/items", new { kind, itemId = (Guid?)null, description = kind + " " + Guid.NewGuid().ToString("N")[..4], quantity, unitPrice = 1000m, resetPrice = false, version = Version(quote) });
            added.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
            quote = await JsonAsync(added);
        }

        await WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlAsync($"UPDATE commercial.quotes SET delivery_days = {deliveryDays} WHERE id = {id}");
            var items = await db.QuoteItems.AsNoTracking().Where(i => i.QuoteId == id).OrderBy(i => i.Sequence).Select(i => i.Id).ToListAsync();
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (center, hours) in lines[i].Labor)
                {
                    db.QuoteItemCosts.Add(new QuoteItemCost { QuoteItemId = items[i], CostType = CostType.Labor, WorkCenterId = center, PositionId = Guid.CreateVersion7(), Quantity = hours, UnitCost = 80m, Total = hours * 80m });
                }
            }

            if (installation)
            {
                db.QuoteItemCosts.Add(new QuoteItemCost { QuoteItemId = items[0], CostType = CostType.Installation, Description = "Instalação", Quantity = 1m, UnitCost = 3000m, Total = 3000m });
            }

            return await db.SaveChangesAsync();
        });
        return id;
    }

    /// <summary>Dois projetos reais no mesmo centro, com a data prometida justa (a do plano de hoje): quem entra na frente atrasa os dois.</summary>
    protected async Task<(HttpClient Owner, Guid Center, Guid First, Guid Second)> TightPairAsync(int offset)
    {
        var (owner, first, second, firstOrder, _) = await QueuePairAsync(Today.AddDays(offset), Today.AddDays(offset + 1));
        var center = await WithDbAsync(db => db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == firstOrder).Select(s => s.WorkCenterId!.Value).FirstAsync());
        await RecalcNowAsync();
        await DrainAsync();
        await SetPromisedAsync(first, (await PlanForecastAsync(first))!.ForecastDate);
        await SetPromisedAsync(second, (await PlanForecastAsync(second))!.ForecastDate);
        return (owner, center, first, second);
    }

    protected Task<string> SimulationStateAsync(Guid quote) => WithDbAsync(async db => string.Join("|",
        await db.ScheduleRuns.CountAsync(), await db.ScheduleStepForecasts.CountAsync(), await db.ScheduleAllocations.CountAsync(), await db.ScheduleProjectForecasts.CountAsync(),
        await db.PlanningQueue.CountAsync(), await db.PlanningQueueChanges.CountAsync(), await db.AuditEntries.CountAsync(), await db.OutboxMessages.CountAsync(),
        await db.ProjectForecasts.CountAsync(), await db.Quotes.AsNoTracking().Where(q => q.Id == quote).Select(q => q.Version).SingleAsync()));

    protected async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = await ScopeAsync();
        return await action(scope.ServiceProvider);
    }
}

/// <summary>
/// PLN-05 com o banco real: simulação no fim e na posição 1 com projetos reais que passam a atrasar (CA 3 e 4), sem gravar nada (CA 5), com
/// o prazo de instalação (CA 4), sem horas (CA 7), só datas para quem não vê custo e sem exigir <c>production.view</c> (CA 9), rate limit da
/// política <c>planning-simulate</c> (CA 10), três camadas e IDOR (CA 11). O plano e a fila são do tenant inteiro: cada teste usa centro e
/// projetos próprios e confere as datas contra o próprio resultado.
/// </summary>
public partial class FabricationBoardTests
{
    [Fact]
    public async Task Simulacao_prazo_no_fim_e_na_posicao_1_mostra_quem_passa_a_atrasar_sem_gravar()
    {
        var (owner, center, first, second) = await TightPairAsync(3400);
        var quote = await SimulationQuoteAsync(owner, 30, false, ("Equipment", 2m, [(center, 20m)]));
        var state = await SimulationStateAsync(quote);

        // CA 3: no fim da fila (padrão), aprovação hoje; ninguém da fila piora.
        var end = await SimulateAsync(owner, quote);
        end.GetProperty("simulated").GetBoolean().Should().BeTrue();
        end.GetProperty("atEnd").GetBoolean().Should().BeTrue();
        end.GetProperty("queuePosition").GetInt32().Should().Be(end.GetProperty("queueSize").GetInt32() + 1);
        DateOf(end, "today").Should().Be(Today);
        var fabrication = DateOf(end, "fabricationEnd")!.Value;
        fabrication.Should().BeOnOrAfter(Today);
        DateOf(end, "deliveryDate").Should().Be(fabrication, "sem custo de instalação, a entrega é o fim da fabricação");
        end.GetProperty("includesInstallation").GetBoolean().Should().BeFalse();
        end.GetProperty("deliveryDays").GetInt32().Should().Be(fabrication.DayNumber - Today.DayNumber);
        end.GetProperty("promisedDays").GetInt32().Should().Be(30);
        DateOf(end, "promisedDate").Should().Be(Today.AddDays(30));
        end.GetProperty("daysBeyondPromise").GetInt32().Should().Be(Math.Max(0, fabrication.DayNumber - Today.AddDays(30).DayNumber));
        var lateAtEnd = end.GetProperty("newlyLateProjects").EnumerateArray().Select(p => p.GetProperty("projectId").GetGuid()).ToList();
        lateAtEnd.Should().NotContain([first, second], "no fim, quem já está na fila não muda");

        // CA 3 e 4: na posição 1, 40 h no centro dos dois projetos de prazo justo: os dois passam a atrasar, com número e nome (projects.view).
        var top = await SimulateAsync(owner, quote, 1);
        top.GetProperty("queuePosition").GetInt32().Should().Be(1);
        top.GetProperty("atEnd").GetBoolean().Should().BeFalse();
        DateOf(top, "fabricationEnd")!.Value.Should().BeOnOrBefore(fabrication);
        var late = top.GetProperty("newlyLateProjects").EnumerateArray().ToList();
        late.Select(p => p.GetProperty("projectId").GetGuid()).Should().Contain([first, second]);
        late.Should().Contain(p => p.GetProperty("projectId").GetGuid() == first && p.GetProperty("number").GetString()!.StartsWith("PRJ-", StringComparison.Ordinal)
            && p.GetProperty("name").GetString()!.Length > 0);
        top.GetProperty("newlyLateCount").GetInt32().Should().Be(late.Count);

        // CA 5: nada gravado (plano, fila, histórico, auditoria, outbox, previsão do projeto e orçamento).
        (await SimulationStateAsync(quote)).Should().Be(state);
        (await WithDbAsync(db => db.Quotes.AsNoTracking().Where(q => q.Id == quote).Select(q => q.DeliveryDays).SingleAsync())).Should().Be(30, "CA 6: só o salvar grava o prazo");

        // CA 9: só quotes.view (sem view_cost, sem production, sem projects.view): só datas e a contagem; nada de custo, horas ou centro.
        var (_, seller) = await ActorAsync("quotes:view");
        var response = await seller.PostAsJsonAsync(SimulationUrl(quote), new { queuePosition = 1 });
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        raw.Should().NotContainAny(["cost", "Cost", "hour", "Hour", "workCenter", "positionId", "price", "margin", center.ToString()], "CA 9: só datas e dias");
        var limited = JsonSerializer.Deserialize<JsonElement>(raw);
        limited.GetProperty("newlyLateProjects").ValueKind.Should().Be(JsonValueKind.Null, "sem projects.view, só a contagem");
        limited.GetProperty("newlyLateCount").GetInt32().Should().Be(late.Count);
        DateOf(limited, "fabricationEnd").Should().Be(DateOf(top, "fabricationEnd"));
    }

    [Fact]
    public void Simulacao_prazo_resposta_so_tem_datas_e_dias()
    {
        // CA 9 (contrato do DTO): nenhum custo, custo-hora, cargo nem hora por centro.
        var names = typeof(QuoteScheduleSimulationView).GetProperties().Select(p => p.Name)
            .Concat(typeof(QuoteSimulationLateProject).GetProperties().Select(p => p.Name)).ToList();
        names.Should().NotBeEmpty();
        names.Should().NotContain(n => n.Contains("Cost", StringComparison.Ordinal) || n.Contains("Hour", StringComparison.Ordinal) || n.Contains("Price", StringComparison.Ordinal)
            || n.Contains("Margin", StringComparison.Ordinal) || n.Contains("WorkCenter", StringComparison.Ordinal) || n.Contains("PositionId", StringComparison.Ordinal)
            || n.Contains("Role", StringComparison.Ordinal) || n.Contains("Step", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Simulacao_prazo_com_instalacao_sem_horas_e_estados_do_orcamento()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var center = await CenterAsync("Simulação");

        // CA 4: com custo de instalação, a entrega é o fim da fabricação + o prazo de instalação (dias úteis, PLN-01b).
        var installed = await SimulationQuoteAsync(owner, null, true, ("Equipment", 1m, [(center, 4m)]), ("Service", 1m, [(center, 50m)]));
        var body = await SimulateAsync(owner, installed);
        body.GetProperty("includesInstallation").GetBoolean().Should().BeTrue();
        var days = await WithScopeAsync(s => s.GetRequiredService<ProductionParametersService>().InstallationDaysAsync());
        var fabrication = DateOf(body, "fabricationEnd")!.Value;
        if (days > 0)
        {
            DateOf(body, "deliveryDate")!.Value.Should().BeAfter(fabrication);
        }

        body.GetProperty("promisedDays").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("daysBeyondPromise").ValueKind.Should().Be(JsonValueKind.Null, "sem prazo prometido não há comparação");

        // CA 7: sem linha de Mão de obra com centro (ou só no Serviço), o motor não é chamado.
        foreach (var empty in new[]
        {
            await SimulationQuoteAsync(owner, 30, false, ("Service", 1m, [(center, 10m)])),
            await SimulationQuoteAsync(owner, 30, false, ("Equipment", 1m, [])),
        })
        {
            var none = await SimulateAsync(owner, empty);
            none.GetProperty("simulated").GetBoolean().Should().BeFalse();
            none.GetProperty("fabricationEnd").ValueKind.Should().Be(JsonValueKind.Null);
        }

        // CA 1: o enviado também simula; CA 11 (IDOR): inexistente 404, revisão antiga e aprovado 409.
        var sent = await SimulationQuoteAsync(owner, 30, false, ("Equipment", 1m, [(center, 2m)]));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.quotes SET status = 'Sent' WHERE id = {sent}"));
        (await SimulateAsync(owner, sent)).GetProperty("simulated").GetBoolean().Should().BeTrue();
        (await owner.PostAsJsonAsync(SimulationUrl(Guid.CreateVersion7()), new { queuePosition = (int?)null })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.quotes SET status = 'Approved' WHERE id = {sent}"));
        var approved = await owner.PostAsJsonAsync(SimulationUrl(sent), new { queuePosition = (int?)null });
        approved.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(approved)).Should().Be("quote_not_simulable");
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.quotes SET status = 'Draft', is_current = false WHERE id = {sent}"));
        var old = await owner.PostAsJsonAsync(SimulationUrl(sent), new { queuePosition = (int?)null });
        old.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(old)).Should().Be("quote_not_current");
    }

    [Fact]
    public async Task Simulacao_prazo_tres_camadas_e_rate_limit()
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var center = await CenterAsync("Simulação");
        var quote = await SimulationQuoteAsync(owner, 30, false, ("Equipment", 1m, [(center, 3m)]));
        var (_, nobody) = await ActorAsync(null);
        var (_, production) = await ActorAsync("production:view,edit");
        var (_, seller) = await ActorAsync("quotes:view");
        foreach (var denied in new[] { nobody, production })
        {
            var response = await denied.PostAsJsonAsync(SimulationUrl(quote), new { queuePosition = (int?)null });
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(response)).Should().Be("forbidden");
        }

        foreach (var module in new[] { "production", "quotes" })
        {
            await SetModuleReleasedAsync(module, false);
            try
            {
                var blocked = await seller.PostAsJsonAsync(SimulationUrl(quote), new { queuePosition = (int?)null });
                blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden, module);
                Code(await JsonAsync(blocked)).Should().Be("module_not_released");
            }
            finally
            {
                await SetModuleReleasedAsync(module, true);
            }
        }

        // CA 10: 20 simulações por minuto por usuário; a 21ª responde 429 com a mensagem da política planning-simulate.
        HttpResponseMessage? limited = null;
        for (var i = 0; i < 25 && limited is null; i++)
        {
            var response = await seller.PostAsJsonAsync(SimulationUrl(quote), new { queuePosition = (int?)null });
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
            else
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK, "sem production.view a simulação responde (CA 9)");
            }
        }

        limited.Should().NotBeNull("acima de 20 por minuto");
        (await JsonAsync(limited!)).GetProperty("title").GetString().Should().Be(PlanningQueueService.SimulateLimitMessage);
        (await owner.PostAsJsonAsync(SimulationUrl(quote), new { queuePosition = (int?)null })).StatusCode.Should().Be(HttpStatusCode.OK, "o limite é por usuário");
    }
}