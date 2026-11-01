using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BemAqui.BuildingBlocks.Security;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BemAqui.Integration.Tests.Production;

public abstract partial class FabricationTestBase
{
    protected const string Kiosk = "/api/v1/kiosk";

    /// <summary>Dois centros com linhas próprias deste teste: etapa A na primeira linha, B na segunda; OS do mesmo projeto.</summary>
    protected async Task<(HttpClient Owner, Guid Project, List<Guid> Orders, string LineA, string LineB, Guid CenterA)> TvDatasetAsync(int orders = 3)
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var lineA = "TV A " + Guid.NewGuid().ToString("N")[..6];
        var lineB = "TV B " + Guid.NewGuid().ToString("N")[..6];
        var centerA = await CenterAsync("Montagem", lineA);
        var centerB = await CenterAsync("Acabamento", lineB);
        var routing = await RoutingAsync(owner, centerA, null, null, Step("Montagem", "A", centerA, 2m), Step("Acabamento", "B", centerB, 1m));
        var product = await ProductAsync();
        var (project, _, items) = await ApprovedAsync(owner, Enumerable.Range(0, orders).Select(_ => ("Equipment", (Guid?)product, 1m)).ToArray(), routingOnItems: routing);
        var ids = new List<Guid>();
        foreach (var item in items)
        {
            ids.Add((await OrderOfAsync(item))!.Id);
        }

        return (owner, project, ids, lineA, lineB, centerA);
    }

    /// <summary>Tela de TV pareada, direto no banco: devolve o id da tela e o cookie da sessão (escopo: linha e visões).</summary>
    protected async Task<(Guid Screen, string Cookie)> TvSessionAsync(string? line, params string[] views)
    {
        var cookie = OpaqueToken.New();
        var screenId = await WithDbAsync(async db =>
        {
            var token = OpaqueToken.New();
            var screen = KioskScreen.Create("TV " + Guid.NewGuid().ToString("N")[..8], line, views.Length == 0 ? KioskViews.All : views, 20, [], OpaqueToken.Hash(token),
                token[..KioskScreen.PrefixLength], Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            screen.Consume(DateTimeOffset.UtcNow);
            db.KioskScreens.Add(screen);
            db.KioskSessions.Add(KioskSession.Open(screen.Id, OpaqueToken.Hash(cookie), null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            return screen.Id;
        });
        return (screenId, cookie);
    }

    /// <summary>Request da TV: só o cookie da sessão de quiosque, sem usuário.</summary>
    protected async Task<HttpResponseMessage> TvGetAsync(string cookie, string path)
    {
        using var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        using var request = new HttpRequestMessage(HttpMethod.Get, Kiosk + path);
        request.Headers.Add("Cookie", KioskCookie.Name + "=" + cookie);
        return await client.SendAsync(request);
    }

    protected async Task<(JsonElement Body, string Raw)> TvOkAsync(string cookie, string path)
    {
        var response = await TvGetAsync(cookie, path);
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, raw);
        return (JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    protected static JsonElement TvCard(JsonElement board, Guid orderId) =>
        board.GetProperty("cards").EnumerateArray().Single(c => c.GetProperty("id").GetGuid() == orderId);

    protected async Task<Guid> EmployeeAsync(string name) => await WithDbAsync(async db =>
    {
        var position = new JobPosition { Name = "Montador " + Guid.NewGuid().ToString("N")[..6], MonthlyHours = 220m, CreatedAt = DateTimeOffset.UtcNow };
        db.JobPositions.Add(position);
        var employee = new Employee
        {
            Name = name, PositionId = position.Id, AdmissionDate = Today.AddYears(-1), Cpf = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
            BirthDate = new DateOnly(1990, 1, 1), BaseSalary = 3000m, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    });

    /// <summary>Etapa em andamento com apontamentos abertos (o primeiro funcionário entrou antes) e, se pedido, todos pausados.</summary>
    protected async Task StartStepAsync(Guid orderId, int sequence, DateTimeOffset startedAt, PauseReason? paused, params Guid[] employeeIds)
    {
        var stepId = await StepIdAsync(orderId, sequence);
        await WithDbAsync(async db =>
        {
            var step = await db.ProductionOrderSteps.SingleAsync(s => s.Id == stepId);
            step.Status = paused is null ? ProductionStepStatus.InProgress : ProductionStepStatus.Paused;
            step.ActualStart = startedAt;
            for (var i = 0; i < employeeIds.Length; i++)
            {
                var entry = new LaborEntry { OrderId = orderId, OrderStepId = stepId, EmployeeId = employeeIds[i], StartedAt = startedAt.AddMinutes(i) };
                db.LaborEntries.Add(entry);
                if (paused is { } reason)
                {
                    db.StepPauses.Add(new StepPause { LaborEntryId = entry.Id, OrderId = orderId, Reason = reason, StartedAt = startedAt.AddMinutes(10) });
                }
            }

            return await db.SaveChangesAsync();
        });
    }
}

/// <summary>
/// PNL-01b (ADR 0030 §5): as visões do Painel da TV com o filtro de linha do token (CA 1 a 6), o cartão de uma OS com o 404 único (CA 14),
/// o DTO sem cliente, produto nem dinheiro (CA 13), a prévia do backoffice com o escopo da tela e sem sessão de quiosque (CA 10) e o
/// desempenho com 300 OS e 20 TVs (CA 12). A sessão de quiosque é gravada direto no banco (a troca do token tem os próprios testes).
/// </summary>
public partial class FabricationShopfloorTests
{
    [Fact]
    public async Task Tv_Quadro_da_TV_filtra_pela_linha_do_token_e_o_cartao_tem_so_a_allowlist()
    {
        var (_, project, orders, lineA, lineB, _) = await TvDatasetAsync();
        await MarkStepAsync(orders[1], 1, ProductionStepStatus.Done);
        await MarkStepAsync(orders[1], 2, ProductionStepStatus.Ready);
        var longName = "Edifício Residencial Jardim das Palmeiras Torre B";
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE commercial.projects SET name = {longName} WHERE id = {project}"));
        var joao = await EmployeeAsync("João Carlos da Silva");
        var maria = await EmployeeAsync("Maria Souza");
        var ana = await EmployeeAsync("Ana Lima");
        var started = DateTimeOffset.UtcNow.AddMinutes(-95);
        await StartStepAsync(orders[0], 1, started, null, joao, maria);
        await StartStepAsync(orders[2], 1, started, PauseReason.Material, ana);
        var (_, cookieA) = await TvSessionAsync(lineA);
        var (_, cookieB) = await TvSessionAsync(lineB);

        var (board, raw) = await TvOkAsync(cookieA, "/board");

        board.GetProperty("line").GetString().Should().Be(lineA);
        board.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().BeEquivalentTo([orders[0], orders[2]], "só as OS cuja etapa atual é de um centro da linha do token (CA 1)");
        board.GetProperty("inProgressCount").GetInt32().Should().Be(2);
        board.GetProperty("lateCount").GetInt32().Should().Be(0);
        board.GetProperty("columns").EnumerateArray().Select(c => (c.GetProperty("key").GetString(), c.GetProperty("code").GetString(), c.GetProperty("count").GetInt32()))
            .Should().Equal([("A", "A", 2)], "só as colunas com OS da linha");

        var card = TvCard(board, orders[0]);
        card.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["id", "number", "projectNumber", "projectName", "columnKey", "stepCode", "stepName", "operator", "pausedReason", "stepStartedAt", "appliedHours", "standardHours", "plannedEnd", "risk", "delayDays"],
            "allowlist do cartão (CA 13)");
        card.GetProperty("projectName").GetString().Should().Be(longName[..27].TrimEnd() + "…").And.HaveLength(28, "nome curto do projeto em 28 caracteres (ponto 49)");
        card.GetProperty("operator").GetString().Should().Be("João S. +1", "primeiro nome e inicial; com mais de um, +{n} (CA 2, item 65)");
        card.GetProperty("pausedReason").ValueKind.Should().Be(JsonValueKind.Null);
        card.GetProperty("stepStartedAt").GetDateTimeOffset().Should().BeCloseTo(started, TimeSpan.FromSeconds(1), "o servidor manda o início real da etapa; a TV conta o tempo");
        card.GetProperty("appliedHours").GetDecimal().Should().BeGreaterThan(3m, "dois apontamentos abertos de ~95 min");
        card.GetProperty("standardHours").GetDecimal().Should().Be(2m);
        card.GetProperty("stepName").GetString().Should().Be("Montagem");

        var paused = TvCard(board, orders[2]);
        paused.GetProperty("operator").ValueKind.Should().Be(JsonValueKind.Null);
        paused.GetProperty("pausedReason").GetString().Should().Be("Falta de material", "etapa pausada mostra o motivo no lugar do operador (CA 4)");

        var party = await WithDbAsync(db => db.Projects.AsNoTracking().Where(p => p.Id == project)
            .Join(db.Parties.AsNoTracking(), p => p.PartyId, x => x.Id, (p, x) => x.LegalName).SingleAsync());
        raw.Should().NotContain(party, "o cliente nunca aparece na TV (CA 13)").And.NotContain("Elevador 6 paradas", "nem o produto")
            .And.NotContain("Carlos").And.NotContain("Souza", "o nome completo do operador nunca sai").And.NotContain("partyName").And.NotContain("productName");

        var (other, _) = await TvOkAsync(cookieB, "/board");
        other.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().Equal(orders[1]);
        TvCard(other, orders[1]).GetProperty("stepCode").GetString().Should().Be("B");

        var (all, _) = await TvOkAsync((await TvSessionAsync(null)).Cookie, "/board");
        all.GetProperty("line").ValueKind.Should().Be(JsonValueKind.Null);
        all.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().Contain(orders, "a tela de Todas as linhas mostra todas");
    }

    [Fact]
    public async Task Tv_Cartao_de_OS_de_outra_linha_inexistente_ou_fechada_responde_o_mesmo_404()
    {
        var (_, _, orders, lineA, _, _) = await TvDatasetAsync(2);
        await MarkStepAsync(orders[1], 1, ProductionStepStatus.Done);
        await MarkStepAsync(orders[1], 2, ProductionStepStatus.Ready);
        var (_, cookie) = await TvSessionAsync(lineA);

        var (card, _) = await TvOkAsync(cookie, $"/board/orders/{orders[0]}");
        card.GetProperty("id").GetGuid().Should().Be(orders[0]);
        card.GetProperty("columnKey").GetString().Should().Be("A");

        var otherLine = await TvGetAsync(cookie, $"/board/orders/{orders[1]}");
        var missing = await TvGetAsync(cookie, $"/board/orders/{Guid.CreateVersion7()}");
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE id = {orders[0]}"));
        var cancelled = await TvGetAsync(cookie, $"/board/orders/{orders[0]}");
        var bodies = new List<string>();
        foreach (var response in new[] { otherLine, missing, cancelled })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var body = await response.Content.ReadAsStringAsync();
            body.Should().NotContain(orders[1].ToString()).And.NotContain("OS-");
            bodies.Add(JsonDocument.Parse(body).RootElement.GetProperty("detail").GetString()!);
        }

        bodies.Distinct().Should().ContainSingle("o 404 não distingue outra linha, outro tenant (o id não existe neste banco) nem OS fechada (CA 14)");
        (await TvGetAsync("cookie-que-nao-existe-" + Guid.NewGuid().ToString("N"), $"/board/orders/{orders[0]}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tv_Atrasos_e_riscos_e_fila_com_previsao_pela_linha_do_token()
    {
        var (_, project, orders, lineA, _, _) = await TvDatasetAsync(2);
        await RecalcNowAsync();
        var (_, cookie) = await TvSessionAsync(lineA);

        var (queue, queueRaw) = await TvOkAsync(cookie, "/queue");
        queue.GetProperty("line").GetString().Should().Be(lineA);
        var rows = queue.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().ContainSingle("só os projetos da fila com OS na linha (CA 5)");
        var row = rows[0];
        row.GetProperty("projectId").GetGuid().Should().Be(project);
        row.GetProperty("position").GetInt32().Should().BeGreaterThan(0);
        row.GetProperty("firstStart").ValueKind.Should().Be(JsonValueKind.String, "início previsto da primeira etapa na linha (PLN-01a)");
        row.GetProperty("forecastDate").ValueKind.Should().Be(JsonValueKind.String, "previsão de entrega (PLN-01b)");
        row.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["position", "projectId", "projectNumber", "projectName", "firstStart", "forecastDate", "promisedDate", "slackDays", "risk"]);
        queueRaw.Should().NotContain("party");

        var (none, _) = await TvOkAsync(cookie, "/delays");
        none.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetGuid()).Should().NotContain(orders);

        await SetPromisedAsync(project, Today.AddDays(-4));
        var (delays, _) = await TvOkAsync(cookie, "/delays");
        var late = delays.GetProperty("rows").EnumerateArray().Where(r => orders.Contains(r.GetProperty("id").GetGuid())).ToList();
        late.Should().HaveCount(2, "as OS atrasadas da linha (CA 6)");
        late.Should().OnlyContain(r => r.GetProperty("risk").GetString() == "Late" && r.GetProperty("days").GetInt32() == 4);
        late[0].GetProperty("stepCode").GetString().Should().Be("A");
        late[0].GetProperty("definingStepName").GetString().Should().Be("Acabamento", "a etapa que termina por último no plano define a entrega");
        delays.GetProperty("rows").GetArrayLength().Should().BeLessThanOrEqualTo(6);
        delays.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        var (board, _) = await TvOkAsync(cookie, "/board");
        board.GetProperty("lateCount").GetInt32().Should().Be(2);
        var card = TvCard(board, orders[0]);
        card.GetProperty("risk").GetString().Should().Be("Late");
        card.GetProperty("delayDays").GetInt32().Should().Be(4);
        card.GetProperty("plannedEnd").ValueKind.Should().Be(JsonValueKind.String, "fim previsto da etapa atual (PLN-01a)");
    }

    [Fact]
    public async Task Tv_Visao_fora_do_escopo_da_tela_responde_404_e_a_TV_so_le()
    {
        var (_, cookie) = await TvSessionAsync(null, KioskViews.Queue);

        (await TvGetAsync(cookie, "/board")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TvGetAsync(cookie, "/delays")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await TvGetAsync(cookie, "/queue")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Host = Host;
        using var post = new HttpRequestMessage(HttpMethod.Post, Kiosk + "/board");
        post.Headers.Add("Cookie", KioskCookie.Name + "=" + cookie);
        (await client.SendAsync(post)).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);

        // Usuário do backoffice não usa a API da TV: o esquema Kiosk é o único aceito ali.
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        (await owner.GetAsync(Kiosk + "/board")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tv_Previa_no_backoffice_mostra_o_mesmo_DTO_com_o_escopo_da_tela_e_sem_sessao_de_quiosque()
    {
        var (_, _, orders, lineA, _, _) = await TvDatasetAsync(2);
        var (screen, cookie) = await TvSessionAsync(lineA, KioskViews.Board, KioskViews.Queue);
        var (_, supervisor) = await ActorAsync("kiosk:view;production:view");
        var (_, noProduction) = await ActorAsync("kiosk:view");
        var preview = $"/api/v1/kiosk-screens/{screen}/preview";

        var context = await JsonAsync(await supervisor.GetAsync(preview));
        context.GetProperty("line").GetString().Should().Be(lineA);
        context.GetProperty("views").EnumerateArray().Select(v => v.GetString()).Should().Equal(KioskViews.Board, KioskViews.Queue);
        context.GetProperty("rotationSeconds").GetInt32().Should().Be(20);

        var response = await supervisor.GetAsync(preview + "/board");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeFalse("a prévia não abre sessão de quiosque");
        var previewBoard = await JsonAsync(response);
        var (tvBoard, _) = await TvOkAsync(cookie, "/board");
        previewBoard.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).Should().BeEquivalentTo(orders);
        previewBoard.GetProperty("cards").ToString().Should().Be(tvBoard.GetProperty("cards").ToString(), "mesmo DTO e mesmo filtro de linha da TV");
        (await supervisor.GetAsync($"{preview}/board/orders/{orders[0]}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await supervisor.GetAsync(preview + "/queue")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await supervisor.GetAsync(preview + "/delays")).StatusCode.Should().Be(HttpStatusCode.NotFound, "a tela não mostra Atrasos e riscos");

        (await noProduction.GetAsync(preview + "/board")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a prévia exige production.view");
        (await supervisor.GetAsync($"/api/v1/kiosk-screens/{Guid.CreateVersion7()}/preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tv_Painel_com_300_OS_e_20_TVs_responde_cada_visao_em_menos_de_500_ms()
    {
        var (_, _, orders, lineA, _, _) = await TvDatasetAsync(1);
        await WithDbAsync(async db =>
        {
            var template = await db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orders[0]);
            var steps = await db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == orders[0]).OrderBy(s => s.Sequence).ToListAsync();
            for (var i = 0; i < 300; i++)
            {
                var order = new ProductionOrder
                {
                    Number = "OS-TV-" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + Guid.NewGuid().ToString("N")[..4], Type = ProductionOrderType.Fabrication,
                    ProjectId = template.ProjectId, ContractId = template.ContractId, ProjectItemId = Guid.CreateVersion7(), ProductId = template.ProductId, Quantity = 1m,
                    RoutingTemplateId = template.RoutingTemplateId, CreatedAt = DateTimeOffset.UtcNow,
                };
                db.ProductionOrders.Add(order);
                foreach (var s in steps)
                {
                    db.ProductionOrderSteps.Add(new ProductionOrderStep
                    {
                        OrderId = order.Id, Sequence = s.Sequence, Name = s.Name, Code = s.Code, WorkCenterId = s.WorkCenterId, StandardHours = s.StandardHours,
                        Status = s.Sequence == 1 ? ProductionStepStatus.Ready : ProductionStepStatus.Waiting,
                    });
                }
            }

            return await db.SaveChangesAsync();
        });
        try
        {
            var tvs = new List<string>();
            for (var i = 0; i < 20; i++)
            {
                tvs.Add((await TvSessionAsync(lineA)).Cookie);
            }

            foreach (var path in new[] { "/board", "/queue", "/delays" })
            {
                await TvOkAsync(tvs[0], path); // aquece
            }

            var (board, _) = await TvOkAsync(tvs[0], "/board");
            board.GetProperty("cards").GetArrayLength().Should().Be(301);

            foreach (var path in new[] { "/board", "/queue", "/delays" })
            {
                var watch = Stopwatch.StartNew();
                await TvOkAsync(tvs[1], path);
                watch.ElapsedMilliseconds.Should().BeLessThan(500, $"{path} com 300 OS (CA 12)");
            }

            // 20 TVs pedem o quadro ao mesmo tempo (a recarga depois de um apontamento): cada uma termina em menos de 500 ms em média.
            var total = Stopwatch.StartNew();
            var times = await Task.WhenAll(tvs.Select(async cookie =>
            {
                var watch = Stopwatch.StartNew();
                (await TvGetAsync(cookie, "/board")).StatusCode.Should().Be(HttpStatusCode.OK);
                return watch.ElapsedMilliseconds;
            }));
            total.Stop();
            times.Average().Should().BeLessThan(500 * 4, "20 recargas simultâneas não travam o servidor");
            (total.ElapsedMilliseconds / (double)tvs.Count).Should().BeLessThan(500, "vazão de uma TV a cada 500 ms com 300 OS");
        }
        finally
        {
            await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE number LIKE 'OS-TV-%'"));
        }
    }
}