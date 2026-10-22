using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BemAqui.HR.Domain;
using BemAqui.Identity.Domain;
using BemAqui.Production.Application;
using BemAqui.Production.Contracts;
using BemAqui.Production.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BemAqui.Integration.Tests.Production;

/// <summary>
/// PRD-03a, app do operador e apontamento: fila e busca (CA 1 a 3), funcionário ativo (CA 4), anexos pela fila (CA 6), iniciar,
/// pausar, retomar, sair e finalizar (CA 7 a 17), supervisor (CA 18 a 22), tempo real (CA 23), DTO sem custo (CA 25), três camadas e
/// IDOR (CA 28, 29), toques repetidos (CA 30) e auditoria (CA 31). Dados no banco desta classe; cada teste usa o próprio roteiro.
/// </summary>
public partial class FabricationTests
{
    private const string Shopfloor = "/api/v1/shopfloor";

    /// <summary>Dois centros (o do operador e outro) e um roteiro C (outro centro) → M (centro do operador, checklist) → Q (qualidade).</summary>
    private async Task<(HttpClient Owner, Guid MyCenter, Guid OtherCenter, Guid Project, List<Guid> Orders)> ShopfloorDatasetAsync(int orders = 1, bool approval = false, string[]? checklist = null)
    {
        var (_, owner) = await ActorAsync(Group.OwnerSeedKey);
        var mine = await CenterAsync("Montagem", "Linha 1");
        var other = await CenterAsync("Corte", "Linha 2");
        object S(string name, string code, Guid center, decimal hours, bool quality = false, bool requires = false, string[]? items = null) =>
            new { id = (Guid?)null, name, code, workCenterId = center, standardHours = hours, isQualityStep = quality, requiresApproval = requires, checklist = items ?? [] };
        var routing = await RoutingAsync(owner, mine, null, null,
            S("Corte", "C", other, 1m, requires: approval), S("Montagem", "M", mine, 2m, items: checklist ?? ["Conferir prumo", "Fixar painéis"]), S("Qualidade", "Q", mine, 0.5m, quality: true));
        var product = await ProductAsync();
        var (project, _, items) = await ApprovedAsync(owner, Enumerable.Range(0, orders).Select(_ => ("Equipment", (Guid?)product, 1m)).ToArray(), routingOnItems: routing);
        var ids = new List<Guid>();
        foreach (var item in items)
        {
            ids.Add((await OrderOfAsync(item))!.Id);
        }

        return (owner, mine, other, project, ids);
    }

    /// <summary>Usuário no grupo dado (Operadores por padrão), ligado a um funcionário ativo cujo cargo tem o centro padrão.</summary>
    private async Task<(Guid UserId, Guid EmployeeId, HttpClient Client, string Name)> OperatorAsync(Guid? center, string group = "operators", bool link = true, bool onLeave = false)
    {
        var (userId, client) = await ActorAsync(group);
        var name = "Diego " + Guid.NewGuid().ToString("N")[..6];
        var employeeId = await WithDbAsync(async db =>
        {
            var position = new JobPosition { Name = "Montador " + Guid.NewGuid().ToString("N")[..6], DefaultWorkCenterId = center, MonthlyHours = 220m, CreatedAt = DateTimeOffset.UtcNow };
            db.JobPositions.Add(position);
            var employee = new Employee
            {
                Name = name, PositionId = position.Id, AdmissionDate = Today.AddYears(-1), Cpf = Random.Shared.NextInt64(10_000_000_000, 99_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture),
                BirthDate = new DateOnly(1990, 1, 1), BaseSalary = 3000m, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Employees.Add(employee);
            if (onLeave)
            {
                db.EmployeeHistory.Add(new EmployeeHistoryEntry
                {
                    EmployeeId = employee.Id, Kind = EmployeeHistoryKind.Leave, EffectiveDate = Today.AddDays(-1), LeaveEndDate = Today.AddDays(5), LeaveType = EmployeeLeaveType.Illness,
                    NewValue = "Afastado", Reason = "Atestado", CreatedAt = DateTimeOffset.UtcNow,
                });
            }

            await db.SaveChangesAsync();
            if (link)
            {
                var user = await db.Users.SingleAsync(u => u.Id == userId);
                user.EmployeeId = employee.Id;
                await db.SaveChangesAsync();
            }

            return employee.Id;
        });
        return (userId, employeeId, client, name);
    }

    private static HttpRequestMessage Tap(string url, object? body = null, Guid? key = null) => Post(url, body ?? new { }, key);

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await JsonAsync(response);
    }

    private static async Task<JsonElement> QueueAsync(HttpClient client) => await OkAsync(await client.GetAsync(Shopfloor + "/queue"));

    private async Task<Guid> StepIdAsync(Guid orderId, int sequence) =>
        await WithDbAsync(db => db.ProductionOrderSteps.AsNoTracking().Where(s => s.OrderId == orderId && s.Sequence == sequence).Select(s => s.Id).SingleAsync());

    private async Task<ProductionOrderStep> StepAsync(Guid stepId) => await WithDbAsync(db => db.ProductionOrderSteps.AsNoTracking().SingleAsync(s => s.Id == stepId));

    private async Task<ProductionOrder> OrderAsync(Guid orderId) => await WithDbAsync(db => db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Id == orderId));

    private async Task<List<LaborEntry>> EntriesAsync(Guid stepId) =>
        await WithDbAsync(db => db.LaborEntries.AsNoTracking().Where(e => e.OrderStepId == stepId).OrderBy(e => e.StartedAt).ToListAsync());

    private async Task<int> OutboxAsync<T>(Guid id) => await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.Type == typeof(T).FullName && m.Payload.Contains(id.ToString())));

    /// <summary>Marca todos os itens do checklist da etapa pelo app (CA 13).</summary>
    private static async Task CheckAllAsync(HttpClient client, Guid stepId)
    {
        var detail = await OkAsync(await client.GetAsync($"{Shopfloor}/steps/{stepId}"));
        foreach (var item in detail.GetProperty("checklist").EnumerateArray())
        {
            (await client.PutAsJsonAsync($"{Shopfloor}/steps/{stepId}/checklist/{item.GetProperty("id").GetGuid()}", new { done = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    private async Task SetModuleReleasedAsync(string module, bool released)
    {
        await using (var scope = tenancy.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<BemAqui.Platform.Infrastructure.Catalog.CatalogDbContext>().TenantModules
                .Where(m => m.TenantId == tenancy.TenantA.Id && m.ModuleCode == module).ExecuteUpdateAsync(s => s.SetProperty(m => m.Enabled, released));
        }

        tenancy.Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantResolver>().Invalidate();
        Factory.Services.GetRequiredService<BemAqui.BuildingBlocks.Tenancy.ITenantResolver>().Invalidate();
    }

    private sealed class ClosedBom : IBomGate
    {
        public Task<bool> IsReleasedAsync(Guid productionOrderId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
    [Fact]
    public async Task Fila_mostra_as_etapas_prontas_com_o_centro_do_operador_primeiro()
    {
        var (_, mine, other, _, orders) = await ShopfloorDatasetAsync(2);
        await MarkStepAsync(orders[1], 1, ProductionStepStatus.Done);
        await MarkStepAsync(orders[1], 2, ProductionStepStatus.Ready);
        var (_, _, client, name) = await OperatorAsync(mine);

        var queue = await QueueAsync(client);

        queue.GetProperty("operator").GetProperty("firstName").GetString().Should().Be("Diego");
        queue.GetProperty("operator").GetProperty("name").GetString().Should().Be(name);
        queue.GetProperty("operator").GetProperty("workCenterId").GetGuid().Should().Be(mine);
        queue.GetProperty("current").ValueKind.Should().Be(JsonValueKind.Null);
        var steps = queue.GetProperty("steps").EnumerateArray().Where(s => orders.Contains(s.GetProperty("orderId").GetGuid())).ToList();
        steps.Select(s => s.GetProperty("stepCode").GetString()).Should().Equal(["M", "C"], "a do meu centro primeiro, depois a do outro centro (D11)");
        steps[0].GetProperty("isMyCenter").GetBoolean().Should().BeTrue();
        steps[1].GetProperty("isMyCenter").GetBoolean().Should().BeFalse();
        steps[1].GetProperty("workCenterId").GetGuid().Should().Be(other);
        steps[0].GetProperty("workCenterName").GetString().Should().StartWith("Montagem");
        steps.Should().OnlyContain(s => s.GetProperty("stepCode").GetString() != "Q", "a etapa de qualidade fica fora da fila até a PRD-05 (CA 16)");

        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE id = {orders[0]}"));
        (await QueueAsync(client)).GetProperty("steps").EnumerateArray().Should().NotContain(s => s.GetProperty("orderId").GetGuid() == orders[0], "OS cancelada sai da fila");
    }

    [Fact]
    public async Task Busca_pelo_numero_acha_as_etapas_e_OS_inexistente_ou_cancelada_nao()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync(2);
        var (_, _, client, _) = await OperatorAsync(mine);
        var number = (await OrderAsync(orders[0])).Number;
        var digits = number.Split('-')[^1].TrimStart('0');

        foreach (var text in new[] { number, digits, number.ToLowerInvariant() })
        {
            var found = await OkAsync(await client.GetAsync($"{Shopfloor}/orders?number={Uri.EscapeDataString(text)}"));
            found.EnumerateArray().Should().Contain(s => s.GetProperty("orderId").GetGuid() == orders[0], text);
        }

        var missing = await client.GetAsync($"{Shopfloor}/orders?number=OS-1999-99999");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await JsonAsync(missing)).GetProperty("detail").GetString().Should().Be("OS não encontrada.");

        var cancelledNumber = (await OrderAsync(orders[1])).Number;
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE id = {orders[1]}"));
        (await client.GetAsync($"{Shopfloor}/orders?number={cancelledNumber}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sem_funcionario_ou_afastado_recebe_403_employee_not_active()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var step = await StepIdAsync(orders[0], 1);
        var (_, _, unlinked, _) = await OperatorAsync(mine, link: false);
        var (_, _, onLeave, _) = await OperatorAsync(mine, onLeave: true);
        var (_, terminatedEmployee, terminated, _) = await OperatorAsync(mine);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE hr.employees SET status = 'Terminated', termination_date = {Today.AddDays(-1)} WHERE id = {terminatedEmployee}"));

        foreach (var client in new[] { unlinked, onLeave, terminated })
        {
            var queue = await client.GetAsync(Shopfloor + "/queue");
            queue.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var problem = await JsonAsync(queue);
            Code(problem).Should().Be("employee_not_active");
            problem.GetProperty("detail").GetString().Should().Be("Seu usuário não está ligado a um funcionário ativo. Fale com o supervisor.");
            Code(await JsonAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")))).Should().Be("employee_not_active");
        }

        (await EntriesAsync(step)).Should().BeEmpty();
    }

    [Fact]
    public async Task Anexo_pelo_app_confere_a_fila_e_o_dono()
    {
        var (owner, mine, _, _, orders) = await ShopfloorDatasetAsync(2);
        var (_, _, client, _) = await OperatorAsync(mine);
        using var pdf = new MultipartFormDataContent { { new ByteArrayContent("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF"u8.ToArray()) { Headers = { ContentType = new("application/pdf") } }, "file", "desenho.pdf" } };
        var uploaded = await owner.PostAsync($"/api/v1/attachments/{InstallationOrderService.AttachmentOwner}/{orders[0]}", pdf);
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());
        var attachment = (await JsonAsync(uploaded)).GetProperty("id").GetGuid();
        var step = await StepIdAsync(orders[0], 1);
        var otherStep = await StepIdAsync(orders[1], 1);

        var list = await OkAsync(await client.GetAsync($"{Shopfloor}/steps/{step}/attachments"));
        list.EnumerateArray().Single().GetProperty("viewable").GetBoolean().Should().BeTrue("pdf, png e jpg abrem na tela");
        var download = await client.GetAsync($"{Shopfloor}/steps/{step}/attachments/{attachment}");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await client.GetAsync($"/api/v1/attachments/{attachment}/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "sem production.view, só pelo app");

        (await client.GetAsync($"{Shopfloor}/steps/{otherStep}/attachments/{attachment}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "o anexo é de outra OS");
        var waiting = await StepIdAsync(orders[0], 2);
        (await client.GetAsync($"{Shopfloor}/steps/{waiting}/attachments/{attachment}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "etapa aguardando não está na fila");
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE id = {orders[0]}"));
        (await client.GetAsync($"{Shopfloor}/steps/{step}/attachments/{attachment}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "OS cancelada sai da fila");
    }

    [Fact]
    public async Task Iniciar_poe_a_etapa_a_OS_e_o_projeto_em_producao_e_registra_quem_apontou()
    {
        var (owner, mine, _, project, orders) = await ShopfloorDatasetAsync();
        var (userId, employeeId, client, _) = await OperatorAsync(mine);
        var step = await StepIdAsync(orders[0], 1);

        var started = await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));

        started.GetProperty("step").GetProperty("mine").GetProperty("entryId").GetGuid().Should().NotBeEmpty();
        var entry = (await EntriesAsync(step)).Single();
        entry.EmployeeId.Should().Be(employeeId);
        entry.RecordedBy.Should().Be(userId);
        entry.OnBehalf.Should().BeFalse();
        entry.EndedAt.Should().BeNull();
        (await StepAsync(step)).Status.Should().Be(ProductionStepStatus.InProgress);
        var order = await OrderAsync(orders[0]);
        order.Status.Should().Be(ProductionOrderStatus.InProgress);
        order.ActualStart.Should().NotBeNull();
        (await OutboxAsync<ProductionStepStarted>(orders[0])).Should().Be(1);
        await DrainAsync();
        (await WithDbAsync(db => db.Projects.AsNoTracking().SingleAsync(p => p.Id == project))).Status.ToString().Should().Be("InProgress");

        (await QueueAsync(client)).GetProperty("current").GetProperty("stepId").GetGuid().Should().Be(step, "a etapa em andamento fica em destaque");
        var card = Card(await BoardAsync(owner, string.Empty), orders[0]);
        card.GetProperty("inExecution").GetBoolean().Should().BeTrue();
        card.GetProperty("operatorInitials").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Inicio_recusa_etapa_nao_pronta_OS_cancelada_e_outro_tenant_sem_restringir_o_centro()
    {
        var (_, mine, other, _, orders) = await ShopfloorDatasetAsync(2);
        var (_, _, client, _) = await OperatorAsync(other);
        var waiting = await StepIdAsync(orders[0], 2);

        var notReady = await client.SendAsync(Tap($"{Shopfloor}/steps/{waiting}/start"));
        notReady.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(notReady)).Should().Be("step_not_ready");
        (await client.SendAsync(Tap($"{Shopfloor}/steps/{Guid.CreateVersion7()}/start"))).StatusCode.Should().Be(HttpStatusCode.NotFound, "etapa de outro tenant ou inexistente");

        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.production_orders SET status = 'Cancelled' WHERE id = {orders[1]}"));
        var cancelled = await client.SendAsync(Tap($"{Shopfloor}/steps/{await StepIdAsync(orders[1], 1)}/start"));
        cancelled.StatusCode.Should().Be(HttpStatusCode.Conflict);
        Code(await JsonAsync(cancelled)).Should().Be("order_cancelled");

        // A primeira etapa é do outro centro e o operador do centro "mine" também inicia (D11).
        var (_, _, fromMine, _) = await OperatorAsync(mine);
        (await fromMine.SendAsync(Tap($"{Shopfloor}/steps/{await StepIdAsync(orders[0], 1)}/start"))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Primeira_etapa_inicia_sem_BOM_e_as_seguintes_pedem_a_BOM_liberada()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, _, client, _) = await OperatorAsync(mine);
        using var bomFactory = Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddScoped<IBomGate, ClosedBom>()));
        var withBom = bomFactory.CreateClient();
        withBom.DefaultRequestHeaders.Host = Host;
        withBom.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        var first = await StepIdAsync(orders[0], 1);
        await MarkStepAsync(orders[0], 1, ProductionStepStatus.Done);
        await MarkStepAsync(orders[0], 2, ProductionStepStatus.Ready);
        var second = await StepIdAsync(orders[0], 2);

        var blocked = await withBom.SendAsync(Tap($"{Shopfloor}/steps/{second}/start"));
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(blocked);
        Code(problem).Should().Be("bom_not_released");
        problem.GetProperty("detail").GetString().Should().Be("A BOM desta OS ainda não foi liberada. Fale com a Engenharia.");

        await MarkStepAsync(orders[0], 1, ProductionStepStatus.Ready);
        await MarkStepAsync(orders[0], 2, ProductionStepStatus.Waiting);
        (await withBom.SendAsync(Tap($"{Shopfloor}/steps/{first}/start"))).StatusCode.Should().Be(HttpStatusCode.OK, "a primeira etapa nunca pede BOM (D12)");
    }

    [Fact]
    public async Task Um_apontamento_aberto_por_funcionario()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync(2);
        var (_, _, client, _) = await OperatorAsync(mine);
        var number = (await OrderAsync(orders[0])).Number;
        await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{await StepIdAsync(orders[0], 1)}/start")));

        var second = await client.SendAsync(Tap($"{Shopfloor}/steps/{await StepIdAsync(orders[1], 1)}/start"));

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await JsonAsync(second);
        Code(problem).Should().Be("labor_already_open");
        problem.GetProperty("detail").GetString().Should().Be($"Você já está na etapa Corte da {number}. Finalize ou pause antes.");
    }
    [Fact]
    public async Task Pausa_com_os_cinco_motivos_aviso_de_falta_de_material_e_retomada()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, _, client, name) = await OperatorAsync(mine);
        var (storekeeper, _) = await ActorAsync("inventory:view,create");
        var step = await StepIdAsync(orders[0], 1);
        var number = (await OrderAsync(orders[0])).Number;
        await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));

        foreach (var reason in new[] { "Machine", "Break", "EndOfShift", "Other", "Material" })
        {
            var paused = await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/pause", new { reason, notes = reason == "Other" ? "Troca de ferramenta" : null })));
            paused.GetProperty("step").GetProperty("mine").GetProperty("pauseReason").GetString().Should().Be(reason);
            (await StepAsync(step)).Status.Should().Be(ProductionStepStatus.Paused, "ninguém mais trabalha nela");
            (await OrderAsync(orders[0])).Status.Should().Be(ProductionOrderStatus.Paused, "nenhuma etapa em andamento");
            Code(await JsonAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/pause", new { reason = "Break" })))).Should().Be("already_paused");
            await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/resume")));
            (await StepAsync(step)).Status.Should().Be(ProductionStepStatus.InProgress);
            (await OrderAsync(orders[0])).Status.Should().Be(ProductionOrderStatus.InProgress);
        }

        var pauses = await WithDbAsync(db => db.StepPauses.AsNoTracking().Where(p => p.OrderId == orders[0]).ToListAsync());
        pauses.Should().HaveCount(5).And.OnlyContain(p => p.EndedAt != null);
        pauses.Single(p => p.Reason == PauseReason.Other).Notes.Should().Be("Troca de ferramenta");
        (await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/pause", new { reason = "Other", notes = new string('x', 121) }))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var notice = await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == storekeeper && n.Type == ShopfloorService.MaterialPauseType).ToListAsync());
        notice.Should().ContainSingle().Which.Title.Should().Be($"{name} parou a {number}, etapa Corte, por falta de material.");
        notice[0].Link.Should().Be(FabricationOrderService.Link + orders[0]);
    }

    [Fact]
    public async Task Finalizar_exige_o_checklist_fecha_o_apontamento_e_libera_a_proxima()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, employeeId, client, _) = await OperatorAsync(mine);
        await MarkStepAsync(orders[0], 1, ProductionStepStatus.Done);
        await MarkStepAsync(orders[0], 2, ProductionStepStatus.Ready);
        var step = await StepIdAsync(orders[0], 2);
        await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));
        await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/pause", new { reason = "Break" })));

        var incomplete = await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/finish", new { forAll = false }));
        incomplete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await JsonAsync(incomplete);
        Code(problem).Should().Be("checklist_incomplete");
        problem.GetProperty("detail").GetString().Should().Be("Marque os 2 itens que faltam.");
        problem.GetProperty("errors").GetProperty("checklist").EnumerateArray().Select(e => e.GetString()).Should().Equal("Conferir prumo", "Fixar painéis");

        await CheckAllAsync(client, step);
        (await WithDbAsync(db => db.ProductionOrderChecklist.AsNoTracking().Where(c => c.StepId == step).ToListAsync()))
            .Should().OnlyContain(c => c.Result == ChecklistResult.Done && c.MarkedBy != null && c.MarkedAt != null);
        var finished = await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/finish", new { forAll = false })));

        finished.GetProperty("step").ValueKind.Should().Be(JsonValueKind.Null, "a etapa concluída sai da fila");
        var entry = (await EntriesAsync(step)).Single();
        entry.EndedAt.Should().NotBeNull();
        entry.CloseKind.Should().Be(LaborCloseKind.Operator);
        (await WithDbAsync(db => db.StepPauses.AsNoTracking().Where(p => p.LaborEntryId == entry.Id).ToListAsync())).Should().OnlyContain(p => p.EndedAt != null, "a pausa aberta fecha junto");
        (await StepAsync(step)).Status.Should().Be(ProductionStepStatus.Done);
        (await StepAsync(step)).ActualEnd.Should().NotBeNull();
        (await StepAsync(await StepIdAsync(orders[0], 3))).Status.Should().Be(ProductionStepStatus.Ready);
        (await OrderAsync(orders[0])).Status.Should().Be(ProductionOrderStatus.Quality, "a próxima é de qualidade: Aguardando qualidade até a PRD-05 (CA 16)");
        var closed = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().Where(m => m.Type == typeof(LaborEntryClosed).FullName && m.Payload.Contains(entry.Id.ToString())).ToListAsync());
        closed.Should().ContainSingle();
        closed[0].Payload.Should().Contain(employeeId.ToString()).And.NotContain("cost", "o custo é da PRD-04");

        var quality = await client.SendAsync(Tap($"{Shopfloor}/steps/{await StepIdAsync(orders[0], 3)}/start"));
        quality.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a qualidade é da PRD-05");
        Code(await JsonAsync(quality)).Should().Be("quality_step");
    }

    [Fact]
    public async Task Dois_operadores_um_sai_e_o_outro_conclui_para_todos()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync(checklist: []);
        var (_, _, ana, _) = await OperatorAsync(mine);
        var (_, beto, betoClient, betoName) = await OperatorAsync(mine);
        var (_, _, caio, _) = await OperatorAsync(mine);
        var step = await StepIdAsync(orders[0], 1);
        foreach (var c in new[] { ana, betoClient, caio })
        {
            await OkAsync(await c.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));
        }

        (await OkAsync(await ana.GetAsync($"{Shopfloor}/steps/{step}"))).GetProperty("people").GetArrayLength().Should().Be(3);
        Code(await JsonAsync(await ana.SendAsync(Tap($"{Shopfloor}/steps/{step}/finish", new { forAll = false })))).Should().Be("others_on_step");

        await OkAsync(await caio.SendAsync(Tap($"{Shopfloor}/steps/{step}/leave")));
        (await StepAsync(step)).Status.Should().Be(ProductionStepStatus.InProgress, "a etapa continua com os outros");
        var done = await OkAsync(await ana.SendAsync(Tap($"{Shopfloor}/steps/{step}/finish", new { forAll = true })));

        done.GetProperty("closedFor").EnumerateArray().Select(e => e.GetString()).Should().Equal(betoName);
        var entries = await EntriesAsync(step);
        entries.Should().OnlyContain(e => e.EndedAt != null);
        entries.Single(e => e.EmployeeId == beto).CloseKind.Should().Be(LaborCloseKind.StepCompleted, "Encerrado por {nome}");
        entries.Count(e => e.CloseKind == LaborCloseKind.Left).Should().Be(1);
        (await StepAsync(step)).Status.Should().Be(ProductionStepStatus.Done);
        (await WithDbAsync(db => db.OutboxMessages.CountAsync(m => m.Type == typeof(LaborEntryClosed).FullName && m.Payload.Contains(step.ToString())))).Should().Be(3);

        var (_, _, solo, _) = await OperatorAsync(mine);
        var next = await StepIdAsync(orders[0], 2);
        await OkAsync(await solo.SendAsync(Tap($"{Shopfloor}/steps/{next}/start")));
        Code(await JsonAsync(await solo.SendAsync(Tap($"{Shopfloor}/steps/{next}/leave")))).Should().Be("only_one_on_step", "o único finaliza ou pausa");
    }

    [Fact]
    public async Task Etapa_com_aprovacao_aguarda_e_quem_apontou_nao_aprova()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync(approval: true);
        var (_, _, client, _) = await OperatorAsync(mine, group: "production_supervision");
        var (approverId, approver) = await ActorAsync("production:view,approve");
        var step = await StepIdAsync(orders[0], 1);
        await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));
        await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/finish", new { forAll = false })));

        var awaiting = await StepAsync(step);
        awaiting.Status.Should().Be(ProductionStepStatus.InProgress, "Aguardando aprovação: em andamento sem apontamento aberto");
        awaiting.AwaitingApprovalSince.Should().NotBeNull();
        (await EntriesAsync(step)).Should().OnlyContain(e => e.EndedAt != null);
        (await WithDbAsync(db => db.Notifications.CountAsync(n => n.UserId == approverId && n.Type == "production.step_awaiting_approval"))).Should().Be(1);
        (await OkAsync(await approver.GetAsync($"{Base}/{orders[0]}/labor-entries"))).GetProperty("awaitingApprovalStepId").GetGuid().Should().Be(step);
        Code(await JsonAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")))).Should().Be("step_not_ready", "aguardando aprovação não recebe apontamento");

        var version = (await OrderAsync(orders[0])).Version;
        var self = await client.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{step}/approve", new { version });
        self.StatusCode.Should().Be(HttpStatusCode.Forbidden, "quem apontou não aprova: há outro aprovador elegível (CA 21)");
        (await JsonAsync(self)).GetProperty("detail").GetString().Should().Be("Quem fez o apontamento não corrige nem aprova a própria etapa.");

        await OkAsync(await approver.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{step}/approve", new { version }));
        var approved = await StepAsync(step);
        approved.Status.Should().Be(ProductionStepStatus.Done);
        approved.AwaitingApprovalSince.Should().BeNull();
        approved.CompletedBy.Should().Be(approverId);
        approved.SelfApproved.Should().BeFalse();
    }

    [Fact]
    public async Task Concluir_etapa_do_supervisor_encerra_os_apontamentos_abertos()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, _, client, _) = await OperatorAsync(mine);
        var (_, supervisor) = await ActorAsync("production:view,approve");
        var step = await StepIdAsync(orders[0], 1);
        await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));
        var version = (await OrderAsync(orders[0])).Version;

        await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/steps/{step}/complete", new { reason = "Operador esqueceu de finalizar.", version }));

        var entry = (await EntriesAsync(step)).Single();
        entry.EndedAt.Should().NotBeNull();
        entry.CloseKind.Should().Be(LaborCloseKind.Supervisor, "Encerrado pelo supervisor");
        (await StepAsync(step)).CompletedBySupervisor.Should().BeTrue();
        (await OutboxAsync<LaborEntryClosed>(entry.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Supervisor_aponta_por_funcionario_com_as_regras_de_sobreposicao_e_7_dias_e_corrige()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, employee, _, employeeName) = await OperatorAsync(mine);
        var (supervisorId, supervisor) = await ActorAsync("production:view,edit,approve;shopfloor:view,create");
        var (_, editorOnly) = await ActorAsync("production:view,edit");
        var step = await StepIdAsync(orders[0], 1);
        var start = DateTimeOffset.UtcNow.AddHours(-3);
        object Body(DateTimeOffset s, DateTimeOffset? e, object[]? pauses = null) => new { employeeId = employee, stepId = step, startedAt = s, endedAt = e, pauses = pauses ?? [], notes = (string?)null };

        (await editorOnly.SendAsync(Post($"{Base}/{orders[0]}/labor-entries", Body(start, start.AddHours(1))))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "exige shopfloor.create também");
        (await supervisor.SendAsync(Post($"{Base}/{orders[0]}/labor-entries", Body(DateTimeOffset.UtcNow.AddHours(1), null)))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "início no futuro");
        (await supervisor.SendAsync(Post($"{Base}/{orders[0]}/labor-entries", Body(start, start.AddHours(-1))))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "fim antes do início");
        var old = await supervisor.SendAsync(Post($"{Base}/{orders[0]}/labor-entries", Body(DateTimeOffset.UtcNow.AddDays(-8), DateTimeOffset.UtcNow.AddDays(-8).AddHours(1))));
        old.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await JsonAsync(old)).GetProperty("detail").GetString().Should().Be("Apontamento retroativo só até 7 dias.");

        var recorded = await OkAsync(await supervisor.SendAsync(Post($"{Base}/{orders[0]}/labor-entries",
            Body(start, start.AddHours(2), [new { reason = "Break", notes = (string?)null, startedAt = start.AddMinutes(30), endedAt = start.AddMinutes(45) }]))));
        var view = recorded.GetProperty("entries").EnumerateArray().Single();
        view.GetProperty("onBehalf").GetBoolean().Should().BeTrue("Apontado por {supervisor}");
        view.GetProperty("netMinutes").GetInt32().Should().Be(105);
        view.GetProperty("pausedMinutes").GetInt32().Should().Be(15);
        view.GetProperty("employeeName").GetString().Should().Be(employeeName);
        var entry = (await EntriesAsync(step)).Single();
        entry.RecordedBy.Should().Be(supervisorId);
        (await StepAsync(step)).Status.Should().Be(ProductionStepStatus.Paused, "iniciada, sem ninguém nela");
        (await OutboxAsync<ProductionStepStarted>(orders[0])).Should().Be(1);
        (await OutboxAsync<LaborEntryClosed>(entry.Id)).Should().Be(1);

        var overlap = await supervisor.SendAsync(Post($"{Base}/{orders[0]}/labor-entries", Body(start.AddHours(1), start.AddHours(2.5))));
        overlap.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await JsonAsync(overlap)).GetProperty("detail").GetString().Should().StartWith($"{employeeName} já tem apontamento entre ");

        object Fix(string? reason) => new { startedAt = start, endedAt = start.AddHours(1.5), pauses = Array.Empty<object>(), reason, version = view.GetProperty("version").GetUInt32() };
        (await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/labor-entries/{entry.Id}/correct", Fix(" "))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await supervisor.PostAsJsonAsync($"{Base}/{Guid.CreateVersion7()}/labor-entries/{entry.Id}/correct", Fix("Erro"))).StatusCode.Should().Be(HttpStatusCode.NotFound, "IDOR pela OS");
        var corrected = await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/labor-entries/{entry.Id}/correct", Fix("Esqueceu de finalizar no tablet.")));
        var after = corrected.GetProperty("entries").EnumerateArray().Single();
        after.GetProperty("netMinutes").GetInt32().Should().Be(90);
        after.GetProperty("correctionReason").GetString().Should().Be("Esqueceu de finalizar no tablet.");
        (await OutboxAsync<LaborEntryCorrected>(entry.Id)).Should().Be(1);
        var history = await OkAsync(await supervisor.GetAsync($"{Base}/{orders[0]}/history"));
        history.GetRawText().Should().Contain(nameof(LaborEntry), "CA 31: a correção entra no histórico da OS com antes e depois");
    }
    [Fact]
    public async Task Quem_apontou_nao_corrige_salvo_sem_outro_aprovador_elegivel()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (supervisorId, _, supervisor, _) = await OperatorAsync(mine, group: "production_supervision");
        await ActorAsync("production:view,approve");
        var step = await StepIdAsync(orders[0], 1);
        await OkAsync(await supervisor.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));
        var entry = (await EntriesAsync(step)).Single();
        object Fix(uint version) => new { startedAt = entry.StartedAt, endedAt = DateTimeOffset.UtcNow, pauses = Array.Empty<object>(), reason = "Ajuste", version };

        var refused = await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/labor-entries/{entry.Id}/correct", Fix(entry.Version));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await JsonAsync(refused)).GetProperty("detail").GetString().Should().Be("Quem fez o apontamento não corrige nem aprova a própria etapa.");

        // Proprietário único (SoleOwnerRule): só ele fica ativo; a correção passa com "Autoaprovado" e o aviso ao Proprietário.
        var others = await WithDbAsync(db => db.Users.Where(u => u.Id != supervisorId && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync());
        var ownerGroup = await WithDbAsync(db => db.Groups.Where(g => g.SeedKey == Group.OwnerSeedKey).Select(g => g.Id).SingleAsync());
        await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Inactive)));
        await WithDbAsync(async db => { db.UserGroups.Add(new UserGroup { UserId = supervisorId, GroupId = ownerGroup }); return await db.SaveChangesAsync(); });
        try
        {
            await using (var scope = await ScopeAsync())
            {
                await scope.ServiceProvider.GetRequiredService<BemAqui.BuildingBlocks.Identity.IPermissionService>().BumpAsync([supervisorId]);
            }

            var allowed = await OkAsync(await supervisor.PostAsJsonAsync($"{Base}/{orders[0]}/labor-entries/{entry.Id}/correct", Fix(entry.Version)));
            allowed.GetProperty("entries").EnumerateArray().Single().GetProperty("selfApproved").GetBoolean().Should().BeTrue("marca Autoaprovado");
            var number = (await OrderAsync(orders[0])).Number;
            (await WithDbAsync(db => db.Notifications.AsNoTracking().Where(n => n.UserId == supervisorId && n.Type == LaborWorkflow.SelfApprovedType).Select(n => n.Title).ToListAsync()))
                .Should().ContainSingle().Which.Should().EndWith($"corrigiu ou aprovou o próprio apontamento na {number}, etapa Corte.");
        }
        finally
        {
            await WithDbAsync(db => db.Users.Where(u => others.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Active)));
            await WithDbAsync(db => db.UserGroups.Where(ug => ug.UserId == supervisorId && ug.GroupId == ownerGroup).ExecuteDeleteAsync());
        }
    }

    [Fact]
    public async Task Apontamentos_abertos_ha_mais_de_12_h()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, _, client, name) = await OperatorAsync(mine);
        var (_, viewer) = await ActorAsync("production:view");
        var step = await StepIdAsync(orders[0], 1);
        await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));
        await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/pause", new { reason = "EndOfShift" })));
        var entry = (await EntriesAsync(step)).Single();
        var list = await OkAsync(await viewer.GetAsync("/api/v1/production/labor-entries/open"));
        list.GetProperty("hours").GetInt32().Should().Be(12);
        list.GetProperty("items").EnumerateArray().Should().NotContain(i => i.GetProperty("id").GetGuid() == entry.Id, "aberto há menos de 12 h");

        var old = DateTimeOffset.UtcNow.AddHours(-13);
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.labor_entries SET started_at = {old} WHERE id = {entry.Id}"));
        await WithDbAsync(db => db.Database.ExecuteSqlAsync($"UPDATE production.step_pauses SET started_at = {old.AddHours(8)} WHERE labor_entry_id = {entry.Id}"));
        var row = (await OkAsync(await viewer.GetAsync("/api/v1/production/labor-entries/open"))).GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == entry.Id);
        row.GetProperty("employeeName").GetString().Should().Be(name);
        row.GetProperty("stepName").GetString().Should().Be("Corte");
        row.GetProperty("lastPauseReason").GetString().Should().Be("EndOfShift");
    }

    [Fact]
    public async Task Inicio_avisa_o_Kanban_e_o_app_depois_do_commit()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, _, client, _) = await OperatorAsync(mine);
        await using var hub = ProductionHubFor(Factory, Host, TokenOf(client));
        var (ids, raw) = Listen(hub);
        await hub.StartAsync();

        await OkAsync(await client.SendAsync(Tap($"{Shopfloor}/steps/{await StepIdAsync(orders[0], 1)}/start")));

        (await ReceivedAsync(ids, orders[0], PushWindow)).Should().BeTrue("o operador está no grupo {tenant}:shopfloor (CA 23)");
        lock (ids)
        {
            raw.Should().OnlyContain(r => r.EnumerateObject().Count() == 1, "só o id da OS");
        }
    }

    [Fact]
    public async Task DTOs_do_app_nao_trazem_custo()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, _, client, _) = await OperatorAsync(mine);
        var step = await StepIdAsync(orders[0], 1);
        var bodies = new[]
        {
            await (await client.GetAsync(Shopfloor + "/queue")).Content.ReadAsStringAsync(),
            await (await client.GetAsync($"{Shopfloor}/steps/{step}")).Content.ReadAsStringAsync(),
            await (await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start"))).Content.ReadAsStringAsync(),
        };
        bodies.Should().OnlyContain(b => !b.Contains("cost", StringComparison.OrdinalIgnoreCase) && !b.Contains("price", StringComparison.OrdinalIgnoreCase) && !b.Contains("salary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Tres_camadas_IDOR_e_funcionario_do_corpo_ignorado()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync(2);
        var (_, none) = await ActorAsync(null);
        var (_, viewOnly) = await ActorAsync("shopfloor:view");
        var (_, ana, anaClient, _) = await OperatorAsync(mine);
        var (_, beto, betoClient, _) = await OperatorAsync(mine);
        var step = await StepIdAsync(orders[0], 1);

        var forbidden = await none.GetAsync(Shopfloor + "/queue");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await JsonAsync(forbidden)).Should().Be("forbidden");
        (await viewOnly.SendAsync(Tap($"{Shopfloor}/steps/{step}/start"))).StatusCode.Should().Be(HttpStatusCode.Forbidden, "apontar exige shopfloor.create");

        await OkAsync(await anaClient.SendAsync(Tap($"{Shopfloor}/steps/{step}/start", new { employeeId = beto })));
        (await EntriesAsync(step)).Single().EmployeeId.Should().Be(ana, "o funcionário nunca vem do corpo (CA 29)");
        Code(await JsonAsync(await betoClient.SendAsync(Tap($"{Shopfloor}/steps/{step}/pause", new { reason = "Break" })))).Should().Be("labor_not_open", "Beto não mexe no apontamento da Ana");
        Code(await JsonAsync(await betoClient.SendAsync(Tap($"{Shopfloor}/steps/{step}/finish", new { forAll = true })))).Should().Be("labor_not_open");
        (await betoClient.GetAsync($"{Shopfloor}/steps/{await StepIdAsync(orders[1], 2)}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "etapa fora da fila");
        (await anaClient.GetAsync($"{Base}/{orders[0]}/labor-entries")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a aba Apontamentos é do backoffice");

        await SetModuleReleasedAsync("shopfloor", false);
        try
        {
            var notReleased = await anaClient.GetAsync(Shopfloor + "/queue");
            notReleased.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            Code(await JsonAsync(notReleased)).Should().Be("module_not_released");
        }
        finally
        {
            await SetModuleReleasedAsync("shopfloor", true);
        }
    }

    [Fact]
    public async Task Dois_toques_em_Iniciar_criam_um_apontamento_so()
    {
        var (_, mine, _, _, orders) = await ShopfloorDatasetAsync();
        var (_, _, client, _) = await OperatorAsync(mine);
        var (_, _, other, _) = await OperatorAsync(mine);
        var step = await StepIdAsync(orders[0], 1);
        var key = Guid.NewGuid();

        (await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start", key: key))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start", key: key))).StatusCode.Should().Be(HttpStatusCode.OK, "a mesma chave devolve o resultado original");
        (await EntriesAsync(step)).Should().ContainSingle();

        // Dois tablets com chaves diferentes: um apontamento aberto por funcionário (índice único) e a OS pela versão (xmin).
        var again = await Task.WhenAll(client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")), client.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));
        again.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        var both = await Task.WhenAll(other.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")), other.SendAsync(Tap($"{Shopfloor}/steps/{step}/start")));
        both.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        both.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
        (await EntriesAsync(step)).Should().HaveCount(2);
    }
}